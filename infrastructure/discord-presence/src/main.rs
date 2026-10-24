// Discord presence bot — polls the status API and mirrors player_count in the bot's presence.
//
// Env:
//   BOT_TOKEN      required
//   STATUS_URL     defaults to the panel status endpoint
//   POLL_INTERVAL  seconds between polls (default 5)
//   MAX_POLLS      stop after N polls; 0 = run forever (default 0, used by the benchmark harness)

use std::env;
use std::sync::Mutex;
use std::time::{Duration, Instant};

use serenity::all::{ActivityData, Context, EventHandler, GatewayIntents, OnlineStatus, Ready, ResumedEvent, ShardStageUpdateEvent};
use serenity::{async_trait, Client};

#[derive(Default)]
struct PollTask(Mutex<Option<tokio::task::JoinHandle<()>>>);

impl PollTask {
    fn replace(&self, task: impl std::future::Future<Output = ()> + Send + 'static) {
        let mut current = self.0.lock().expect("poll task lock poisoned");
        if let Some(previous) = current.take() {
            previous.abort();
        }
        *current = Some(tokio::spawn(task));
    }
}

struct Handler {
    poll_task: PollTask,
}

#[async_trait]
impl EventHandler for Handler {
    async fn ready(&self, ctx: Context, ready: Ready) {
        println!("READY as {}", ready.user.name);
        // A replacement shard runner has a new messenger. Never keep sending
        // through the Context captured by the first READY of the process.
        self.poll_task.replace(poll_loop(ctx));
    }

    async fn resume(&self, ctx: Context, _: ResumedEvent) {
        println!("RESUMED: refreshing presence with current gateway context");
        self.poll_task.replace(poll_loop(ctx));
    }

    async fn shard_stage_update(&self, _: Context, event: ShardStageUpdateEvent) {
        println!("gateway stage {:?} -> {:?}", event.old, event.new);
    }
}

fn needs_presence_refresh(previous: Option<&Option<i64>>, current: &Option<i64>, elapsed: Duration) -> bool {
    previous != Some(current) || elapsed >= Duration::from_secs(60)
}

fn presence_text(player_count: Option<i64>) -> String {
    match player_count {
        None => "Server is Offline".to_string(),
        Some(n) => format!("{} player{} online", n, if n == 1 { "" } else { "s" }),
    }
}

fn presence_status(player_count: Option<i64>) -> OnlineStatus {
    match player_count {
        Some(_) => OnlineStatus::Online,
        None => OnlineStatus::DoNotDisturb,
    }
}

async fn poll_loop(ctx: Context) {
    let status_url = env::var("STATUS_URL")
        .unwrap_or_else(|_| "https://panel.bnl.danyil.cc/api/public/status".to_string());
    let poll_interval: f64 = env::var("POLL_INTERVAL")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(5.0);
    let max_polls: u64 = env::var("MAX_POLLS")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(0);

    let http = reqwest::Client::builder()
        .timeout(Duration::from_secs(10))
        .build()
        .expect("failed to build http client");

    // Cache the state represented by the response, rather than the rendered
    // presence. A repeated result (including an offline/error result) does not
    // need any Discord gateway work.
    let mut last_fetched_state: Option<Option<i64>> = None;
    let mut last_presence = Instant::now();
    let mut polls: u64 = 0;

    loop {
        let player_count: Option<i64> = match http.get(&status_url).send().await {
            Ok(resp) => match resp.error_for_status() {
                Ok(resp) => match resp.json::<serde_json::Value>().await {
                    Ok(v) => v.get("player_count").and_then(|p| p.as_i64()),
                    Err(e) => {
                        eprintln!("fetch failed: {e}");
                        None
                    }
                },
                Err(e) => {
                    eprintln!("fetch failed: {e}");
                    None
                }
            },
            Err(e) => {
                eprintln!("fetch failed: {e}");
                None
            }
        };

        // Do nothing for an unchanged fetched state. Presence updates are rate
        // limited (5 per 20s per session), so re-sending an identical value is waste.
        // Reassert once a minute as well as on changes. The cache is local to
        // this gateway session and is discarded after READY or RESUMED.
        let updated = needs_presence_refresh(last_fetched_state.as_ref(), &player_count, last_presence.elapsed());
        if updated {
            let text = presence_text(player_count);
            ctx.set_presence(
                Some(ActivityData::watching(&text)),
                presence_status(player_count),
            );
            last_fetched_state = Some(player_count);
            last_presence = Instant::now();
        }

        polls += 1;
        let pc = player_count
            .map(|n| n.to_string())
            .unwrap_or_else(|| "None".to_string());
        // set_presence queues a gateway command; it is not a Discord ACK.
        println!("poll {polls} player_count={pc} presence_queued={updated}");

        if max_polls > 0 && polls >= max_polls {
            std::process::exit(0);
        }

        tokio::time::sleep(Duration::from_secs_f64(poll_interval)).await;
    }
}

#[cfg(test)]
mod tests {
    use super::{presence_status, presence_text, needs_presence_refresh, PollTask};
    use std::time::Duration;
    use serenity::all::OnlineStatus;

    #[test]
    fn offline_status_has_the_requested_presence_text() {
        assert_eq!(presence_text(None), "Server is Offline");
        assert_eq!(presence_status(None), OnlineStatus::DoNotDisturb);
    }

    #[test]
    fn online_status_has_an_online_badge() {
        assert_eq!(presence_status(Some(0)), OnlineStatus::Online);
    }

    #[test]
    fn presence_cache_is_reset_on_new_session_and_refreshed_periodically() {
        for state in [None, Some(0), Some(1)] {
            assert!(needs_presence_refresh(None, &state, Duration::ZERO));
            assert!(!needs_presence_refresh(Some(&state), &state, Duration::from_secs(10)));
            assert!(needs_presence_refresh(Some(&state), &state, Duration::from_secs(60)));
        }
        assert!(needs_presence_refresh(Some(&Some(0)), &Some(1), Duration::ZERO));
        assert!(needs_presence_refresh(Some(&Some(1)), &None, Duration::ZERO));
    }

    #[tokio::test]
    async fn reconnect_replaces_instead_of_duplicating_poll_task() {
        let slot = PollTask::default();
        slot.replace(std::future::pending());
        let old = slot.0.lock().unwrap().as_ref().unwrap().abort_handle();
        slot.replace(std::future::pending());
        tokio::task::yield_now().await;
        assert!(old.is_finished());
        let current = slot.0.lock().unwrap().take().unwrap();
        assert!(!current.is_finished());
        current.abort();
        assert!(current.await.unwrap_err().is_cancelled());
    }
}

#[tokio::main]
async fn main() {
    let token = match env::var("BOT_TOKEN") {
        Ok(t) if !t.is_empty() => t,
        _ => {
            eprintln!("BOT_TOKEN is not set");
            std::process::exit(1);
        }
    };

    let handler = Handler {
        poll_task: PollTask::default(),
    };

    let mut client = Client::builder(&token, GatewayIntents::GUILDS)
        .event_handler(handler)
        .await
        .expect("failed to create client");

    if let Err(e) = client.start().await {
        eprintln!("client error: {e}");
        std::process::exit(1);
    }
}
