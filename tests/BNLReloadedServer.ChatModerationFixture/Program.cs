using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.Authentication;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

var temp=Path.Combine(Path.GetTempPath(),"bnl-chat-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(temp,"Configs"));
Directory.SetCurrentDirectory(temp);
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
var now=DateTimeOffset.UtcNow;
var path=Path.Combine(temp,"Configs","chat_bans.json");
var policy=new ChatBanList(path);
Check(policy.Active(4,now)==null,"no file, no ban");
void Write(string text,int seconds){File.WriteAllText(path,text);File.SetLastWriteTimeUtc(path,now.UtcDateTime.AddSeconds(seconds));}
var ban=new ChatBanList.Ban(4,now.AddHours(24),"Impersonating another player in global chat.");
var valid=JsonSerializer.Serialize(new[]{ban});
Write(valid,1);
Check(policy.Active(4,now)!=null,"verified account restricted");
Check(policy.Active(110,now)==null,"victim unaffected");
Check(policy.Active(11,now)==null,"bystander unaffected");
Check(policy.Active(4,ban.UntilUtc.AddTicks(-1))!=null,"active until expiry");
Check(policy.Active(4,ban.UntilUtc)==null,"expires exactly at 24h");
Check(new ChatBanList(path).Active(4,now)!=null,"survives restart");
Write("broken",2);Check(policy.Active(4,now)!=null,"bad edit retains ban");
Write("[null]",3);Check(policy.Active(4,now)!=null,"null entry retains ban");
Write("[]",4);Check(policy.Active(4,now)==null,"operator can lift restriction");
Write(valid,5);
var attack="<color=#5BBC4D>Prbtt</color>: forged~{player_alert_mark}~";
var safe=PlayerChatText.Plain(attack);
Check(!safe.Contains("~{player_alert_mark}~")&&!safe.Contains('<'),"observed exploit neutralized");
Check(PlayerChatText.Plain("hello :)")=="hello :)","ordinary text unchanged");
Check(!PlayerChatText.Plain("a\r\nPrbtt:\u2028fake\u202e").Any(c=>c=='\n'||c=='\r'||c=='\u2028'||c=='\u202e'),"extra lines and bidi controls removed");
Check(PlayerChatText.Plain(safe)==safe,"sanitization idempotent");
Check(ban.Warning.Contains("UTC")&&ban.Warning.Contains("impersonate")&&ban.Warning.Contains("still play"),"popup includes expiry and restriction scope");
// Real ingress methods must reject before resolving rooms/recipients/clan membership.
var region=(RegionServerDatabase)RuntimeHelpers.GetUninitializedObject(typeof(RegionServerDatabase));
var flags=BindingFlags.NonPublic|BindingFlags.Instance;
typeof(RegionServerDatabase).GetField("chatWarningTimes",flags)!.SetValue(region,new ConcurrentDictionary<uint,long>(new[]{new KeyValuePair<uint,long>(4,Environment.TickCount64)}));
Check(!region.SendMessage(4,new RoomIdGlobal(),"test"),"global ingress blocked");
Check(!region.SendMessage(4,new RoomIdSquad(),"test"),"room ingress blocked");
Check(region.SendMessage(4,110,"test")!=null,"private ingress blocked");
Check(!region.SendClanChat(4,"test"),"clan ingress blocked");
Console.WriteLine($"PASS {checks} chat moderation checks");
