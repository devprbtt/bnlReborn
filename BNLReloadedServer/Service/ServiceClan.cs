using BNLReloadedServer.Clans;
using BNLReloadedServer.Database;
using BNLReloadedServer.Logging;
using BNLReloadedServer.Servers;

namespace BNLReloadedServer.Service;

public interface IServiceClan : IService
{
    bool SupportsClans { get; }
    void SendExtension(byte message, string json) { }
    void SendState(ClanStateMessage state);
}

/// <summary>
/// Clan protocol on the region connection (service 16; the client already uses 15 for EAC). A client opts in with
/// Hello; until then the server sends nothing on this service, so older clients never see clan traffic.
/// Every request carries a client-chosen id echoed in its Result; state arrives as a full snapshot on change.
/// </summary>
public class ServiceClan(ISender sender) : IServiceClan
{
    public const int ProtocolVersion = 1;
    private const int MaxRequestsPerWindow = 10;
    private static readonly TimeSpan RequestWindow = TimeSpan.FromSeconds(10);

    private enum ClientMessage : byte
    {
        Hello = 0, Create = 1, Invite = 2, Accept = 3, Decline = 4, Leave = 5,
        Kick = 6, SetRank = 7, Transfer = 8, Rename = 9, Disband = 10, SetTagColor = 11, Browse = 12, InvitePlayer = 13, PlayerTags = 14
    }

    private enum ServerMessage : byte { State = 0, Result = 1 }

    public bool SupportsClans { get; private set; }
    public bool SupportsCompetition { get; private set; }

    private long _lastPoll;
    private readonly Queue<DateTimeOffset> _recent = new();

    private static BinaryWriter CreateWriter()
    {
        var writer = new BinaryWriter(new MemoryStream());
        writer.Write((byte)ServiceId.ServiceClan);
        return writer;
    }

    public bool Receive(BinaryReader reader)
    {
        var message = (ClientMessage)reader.ReadByte();
        if (message == ClientMessage.Hello)
        {
            var version = reader.ReadInt32();
            var first = !SupportsClans;
            SupportsClans = version >= 1 && ClanHub.Started;
            SupportsCompetition = version >= 2 && SupportsClans;
            if (!SupportsClans || sender.AssociatedPlayerId is not { } id) return true;
            // The first Hello of a connection announces the player online to their clan; later ones (the client
            // re-sends Hello when the CLAN page opens) refresh only this player's view, so they cannot flood a clan.
            if (first) ClanHub.PushClanOf(id);
            else ClanHub.Push(id);
            if (SupportsCompetition && Databases.RegionServerDatabase is RegionServerDatabase draftRegion) draftRegion.SendClanDraft(id);
            return true;
        }
        if (!SupportsClans || sender.AssociatedPlayerId is not { } playerId) return true;
        var requestId = reader.ReadUInt16();
        if ((byte)message >= 15 && !SupportsCompetition) { SendResult(requestId, ClanResult.NotAllowed); return true; }
        if ((byte)message == 15)
        {
            byte command = reader.ReadByte(); int roomId = reader.ReadInt32(); uint key = reader.ReadUInt32();
            if (command == 0) { long now = Environment.TickCount64; if (now - _lastPoll < 500) { SendResult(requestId, ClanResult.Ok); return true; } _lastPoll = now; }
            else if (Throttled()) { SendResult(requestId, ClanResult.NotAllowed); return true; }
            SendExtension(4, ClanCompetition.Handle(playerId, command, roomId, key));
            SendResult(requestId, ClanResult.Ok); return true;
        }
        if (Throttled())
        {
            SendResult(requestId, ClanResult.NotAllowed);
            return true;
        }
        if ((byte)message == 16)
        {
            SendExtension(5, ClanRatings.Leaderboard()); SendResult(requestId, ClanResult.Ok); return true;
        }
        if ((byte)message == 17)
        {
            bool sent = Databases.RegionServerDatabase is RegionServerDatabase region && region.SendClanChat(playerId, reader.ReadString());
            SendResult(requestId, sent ? ClanResult.Ok : ClanResult.NotAllowed); return true;
        }
        var store = ClanHub.Store;
        if (message == ClientMessage.PlayerTags)
        {
            int count = reader.ReadUInt16();
            if (count > 64) { SendResult(requestId, ClanResult.NotAllowed); return true; }
            using var writer = CreateWriter();
            writer.Write((byte)3); writer.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                uint id = reader.ReadUInt32();
                var tag = store.TagAndColorOf(id);
                writer.Write(id); writer.Write(tag?.Tag ?? ""); writer.Write(tag?.Color ?? 0u);
            }
            sender.Send(writer);
            SendResult(requestId, ClanResult.Ok);
            return true;
        }
        if (message == ClientMessage.Browse)
        {
            string query = reader.ReadString();
            if (query.Length > 64) { SendResult(requestId, ClanResult.InvalidName); return true; }
            ClanHub.Browse(query).ContinueWith(t =>
            {
                if (t.IsFaulted) { Log.Error(LogCat.Player, "Clan browse failed", t.Exception!); SendResult(requestId, ClanResult.NotAllowed); return; }
                using var writer = CreateWriter();
                writer.Write((byte)2);
                writer.Write(requestId);
                writer.Write((ushort)t.Result.Length);
                foreach (var state in t.Result) WriteState(writer, state);
                sender.Send(writer);
                SendResult(requestId, ClanResult.Ok);
            });
            return true;
        }
        Task<ClanResult> work = message switch
        {
            ClientMessage.Create => Create(store, playerId, reader.ReadString(), reader.ReadString()),
            ClientMessage.Invite => Invite(store, playerId, reader.ReadString()),
            ClientMessage.InvitePlayer => InvitePlayer(store, playerId, reader.ReadUInt32()),
            ClientMessage.Accept => store.Accept(playerId, reader.ReadInt32()),
            ClientMessage.Decline => store.Decline(playerId, reader.ReadInt32()),
            ClientMessage.Leave => store.Leave(playerId),
            ClientMessage.Kick => store.Kick(playerId, reader.ReadUInt32()),
            ClientMessage.SetRank => SetRank(store, playerId, reader.ReadUInt32(), reader.ReadByte()),
            ClientMessage.Transfer => store.TransferLeadership(playerId, reader.ReadUInt32()),
            ClientMessage.Rename => store.Rename(playerId, reader.ReadString(), reader.ReadString()),
            ClientMessage.Disband => store.Disband(playerId),
            ClientMessage.SetTagColor => store.SetTagColor(playerId, reader.ReadUInt32()),
            _ => Task.FromResult(ClanResult.NotAllowed)
        };
        work.ContinueWith(t =>
        {
            if (t.IsFaulted) Log.Error(LogCat.Player, $"Clan request {message} from player {playerId} failed", t.Exception!);
            SendResult(requestId, t.IsFaulted ? ClanResult.NotAllowed : t.Result);
        });
        return true;
    }

    private static async Task<ClanResult> Create(ClanStore store, uint playerId, string name, string tag) =>
        (await store.Create(playerId, name, tag)).Result;

    private static async Task<ClanResult> Invite(ClanStore store, uint playerId, string name)
    {
        var target = await ClanHub.FindPlayer(name);
        if (target is not { } targetId || targetId == playerId) return ClanResult.NoSuchPlayer;
        return await store.Invite(playerId, targetId);
    }

    private static Task<ClanResult> SetRank(ClanStore store, uint playerId, uint target, byte rank) =>
        Enum.IsDefined(typeof(ClanRank), rank) ? store.SetRank(playerId, target, (ClanRank)rank) : Task.FromResult(ClanResult.NotAllowed);

    private static async Task<ClanResult> InvitePlayer(ClanStore store, uint playerId, uint targetId)
    {
        if (targetId == playerId || (await Databases.MasterServerDatabase.GetSearchResults(new List<uint> { targetId })).Count != 1)
            return ClanResult.NoSuchPlayer;
        return await store.Invite(playerId, targetId);
    }

    private bool Throttled()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_recent)
        {
            while (_recent.Count > 0 && now - _recent.Peek() > RequestWindow) _recent.Dequeue();
            if (_recent.Count >= MaxRequestsPerWindow) return true;
            _recent.Enqueue(now);
            return false;
        }
    }

    private void SendResult(ushort requestId, ClanResult result)
    {
        using var writer = CreateWriter();
        writer.Write((byte)ServerMessage.Result);
        writer.Write(requestId);
        writer.Write((byte)result);
        sender.Send(writer);
    }

    public void SendExtension(byte message, string json)
    {
        if (!SupportsCompetition) return;
        using var writer = CreateWriter(); writer.Write(message); writer.Write(json); sender.Send(writer);
    }

    public void SendState(ClanStateMessage state)
    {
        if (!SupportsClans) return;
        using var writer = CreateWriter();
        writer.Write((byte)ServerMessage.State);
        WriteState(writer, state);
        sender.Send(writer);
    }

    // Shared with the fixture, which checks the client decoder's expectations against this layout.
    public static void WriteState(BinaryWriter writer, ClanStateMessage state)
    {
        writer.Write((byte)ProtocolVersion);
        writer.Write(state.Clan != null);
        if (state.Clan is { } clan)
        {
            writer.Write(clan.Id);
            writer.Write(clan.Name);
            writer.Write(clan.Tag);
            writer.Write(clan.TagColor);
            writer.Write(clan.LeaderId);
            writer.Write((byte)clan.MyRank);
            writer.Write(clan.RenamedAt?.ToUnixTimeSeconds() ?? 0L);
            writer.Write((ushort)clan.Members.Length);
            foreach (var m in clan.Members)
            {
                writer.Write(m.PlayerId);
                writer.Write(m.Name);
                writer.Write((byte)m.Rank);
                writer.Write(m.Activity ?? string.Empty);   // empty = offline
                writer.Write(m.JoinedAt.ToUnixTimeSeconds());
            }
            writer.Write((ushort)clan.Outgoing.Length);
            foreach (var o in clan.Outgoing)
            {
                writer.Write(o.PlayerId);
                writer.Write(o.Name);
                writer.Write(o.CreatedAt.ToUnixTimeSeconds());
            }
        }
        writer.Write((ushort)state.Invites.Length);
        foreach (var i in state.Invites)
        {
            writer.Write(i.ClanId);
            writer.Write(i.ClanName);
            writer.Write(i.Tag);
            writer.Write(i.InviterName);
            writer.Write(i.CreatedAt.ToUnixTimeSeconds());
        }
    }
}
