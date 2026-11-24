using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ServerTypes;

int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
var maps=Enumerable.Range(1,7).Select(i=>new CardMap{Id="preference_"+i,Key=new Key("preference_"+i)}).ToArray();
var valid=maps.Select(m=>m.Key.Hash).ToHashSet();
Dictionary<uint,bool> Ballot(int approved)=>maps.ToDictionary(m=>m.Key.Hash,m=>Array.IndexOf(maps,m)<approved);
IReadOnlyDictionary<uint,IReadOnlyDictionary<uint,bool>> Votes(params Dictionary<uint,bool>[] ballots)=>ballots.Select((b,i)=>(b,i)).ToDictionary(p=>(uint)p.i+1,p=>(IReadOnlyDictionary<uint,bool>)p.b);
List<Key> Pick(IReadOnlyDictionary<uint,IReadOnlyDictionary<uint,bool>> votes,int seed=42)=>MapVoteSelection.SelectForPlayers(maps,3,GameRankingType.Friendly,new Random(seed),votes);
var legacy=MapVoteSelection.Select(maps,3,GameRankingType.Friendly,new Random(42));
Check(Pick(Votes()).SequenceEqual(legacy),"old clients retain exact seeded legacy draw");
Check(Pick(Votes(Ballot(0))).SequenceEqual(legacy),"all unmarked means server decides");
Check(Pick(Votes(new Dictionary<uint,bool>{{uint.MaxValue,true}})).SequenceEqual(legacy),"ineligible selections abstain");
for(int seed=0;seed<250;seed++){
 var chosen=Pick(Votes(Ballot(3),Ballot(3),Ballot(7)),seed);
 Check(chosen.Count==3 && chosen.All(k=>maps.Take(3).Any(m=>m.Key==k)),"strict majority excludes other maps");
 var one=Pick(Votes(Ballot(1)),seed);
 Check(one.Count==3&&one.Distinct().Count()==3&&one.Contains(maps[0].Key),"server fills one approved map to three");
 var two=Pick(Votes(Ballot(2)),seed);
 Check(two.Count==3&&two.Contains(maps[0].Key)&&two.Contains(maps[1].Key),"server fills two approved maps to three");
 var tied=Pick(Votes(Ballot(3),maps.ToDictionary(m=>m.Key.Hash,m=>Array.IndexOf(maps,m)>=3)),seed);
 Check(tied.Count==3&&tied.Distinct().Count()==3,"ties always produce three distinct maps");
 Check(tied.SequenceEqual(Pick(Votes(Ballot(3),maps.ToDictionary(m=>m.Key.Hash,m=>Array.IndexOf(maps,m)>=3)),seed)),"ties use server random deterministically");
}
MapPreferences.Set(4001,0,Ballot(3),valid);
MapPreferences.Set(4001,1,Ballot(6),valid);
MapPreferences.Set(4002,0,new Dictionary<uint,bool>{{uint.MaxValue,true},{maps[0].Key.Hash,false}},valid);
var snapshot=MapPreferences.Snapshot(new uint[]{4001,4001,4002},GameRankingType.Friendly);
Check(snapshot.Count==2&&snapshot[4002].Count==1,"actual roster distinct and unknown maps filtered");
MapPreferences.Set(4001,0,Ballot(0),valid);
Check(snapshot[4001].Values.Count(v=>v)==3,"saved snapshot is unaffected by later preferences");
Check(MapPreferences.Snapshot(new uint[]{4001},GameRankingType.Ranked)[4001].Values.Count(v=>v)==6,"mode isolation");
Check(MapPreferences.Snapshot(new uint[]{4001},GameRankingType.None).Count==0,"custom games ignore preferences");
MapPreferences.Set(4001,0,Ballot(3),valid);
PlayerQueueData Player(uint id)=>new(id,Guid.NewGuid(),new Moserware.Skills.Rating(25,8),DateTimeOffset.UtcNow,null,new Key("mode_friendly"));
var match=new MatchmakerInitiator(new CardGameMode{Ranking=GameRankingType.Friendly,PlayersPerTeam=4},[Player(4001)],[Player(4002)]);
MapPreferences.Set(4001,0,Ballot(0),valid);
MapPreferences.Set(4003,0,Ballot(7),valid);
match.AddPlayer(Player(4003),TeamType.Team1);
Check(match.MapPreferencesAtPop.Count==2&&match.MapPreferencesAtPop[4001].Values.Count(v=>v)==3&&!match.MapPreferencesAtPop.ContainsKey(4003),"actual match pop freezes ballots before late edit or backfill");
MapPreferences.Set(4001,0,Ballot(1),valid);
Check(snapshot[4001].Count(p=>p.Value)==3,"pop copies immutable after later edits");
Check(MapPreferences.Snapshot(new uint[]{4001},GameRankingType.Ranked)[4001].Count(p=>p.Value)==6,"modes isolated");
Check(MapPreferences.Snapshot(new uint[]{4001},GameRankingType.None).Count==0,"customs unaffected");
Check(!MapPreferences.Snapshot(new uint[]{4002},GameRankingType.Friendly).ContainsKey(4001),"other queued players excluded");
byte[] Wire(byte version=1,byte mode=0,ushort count=2,uint key1=10,byte value1=1,uint key2=20,byte value2=0){using var ms=new MemoryStream();using var w=new BinaryWriter(ms);w.Write(version);w.Write(mode);w.Write(count);w.Write(key1);w.Write(value1);w.Write(key2);w.Write(value2);return ms.ToArray();}
bool Read(byte[] bytes){using var r=new BinaryReader(new MemoryStream(bytes));return MapPreferences.TryRead(r,out _,out _);}
Check(Read(Wire()),"wire accepts full Boolean choices");
foreach(var bad in new[]{Wire(version:2),Wire(mode:3),Wire(count:513),Wire(key1:0),Wire(key2:10),Wire(value1:2)})Check(!Read(bad),"malformed payload rejected");
Check(Read(Wire(mode:2)),"independent 5v5 mode accepted with legacy wire framing");
Check(MapPreferences.Version==2&&MapPreferences.WireVersion==1,"capability advertises three slots while preserving wire format");
MapPreferences.Set(5001,0,Ballot(3),valid);
MapPreferences.Set(5001,1,Ballot(6),valid);
MapPreferences.Set(5001,2,maps.ToDictionary(m=>m.Key.Hash,m=>Array.IndexOf(maps,m)>=3),valid);
var four=new MatchmakerInitiator(new CardGameMode{Ranking=GameRankingType.Friendly,PlayersPerTeam=4},[Player(5001)],[]);
var five=new MatchmakerInitiator(new CardGameMode{Ranking=GameRankingType.Friendly,PlayersPerTeam=4},[Player(5001)],[],5);
Check(four.MapPreferencesAtPop[5001].Count(p=>p.Value)==3,"4v4 pop captures only 4v4 preferences");
Check(five.PlayersPerTeam==5&&five.MapPreferencesAtPop[5001].Count(p=>p.Value)==4,"automatic 5v5 pop uses actual size despite 4v4 game card");
for(int seed=0;seed<100;seed++) {
 Check(Pick(four.MapPreferencesAtPop,seed).All(k=>maps.Take(3).Any(m=>m.Key==k)),"4v4 ballot follows 4v4 majority");
 Check(Pick(five.MapPreferencesAtPop,seed).All(k=>maps.Skip(3).Any(m=>m.Key==k)),"5v5 ballot follows independent 5v5 majority");
}
MapPreferences.Set(5001,2,Ballot(0),valid);
Check(five.MapPreferencesAtPop[5001].Values.Count(v=>v)==4,"5v5 pop is immutable after late changes");
Check(MapPreferences.Snapshot(new uint[]{5001},GameRankingType.Friendly,4)[5001].Values.Count(v=>v)==3,"5v5 edits leave 4v4 unchanged");
Check(MapPreferences.Snapshot(new uint[]{5001},GameRankingType.Ranked,5)[5001].Values.Count(v=>v)==6,"Ranked remains independent of Casual size");
MapPreferences.Set(5002,0,Ballot(3),valid);
Check(MapPreferences.Snapshot(new uint[]{5002},GameRankingType.Friendly,5).Count==0,"legacy shared ballot does not leak into 5v5");
Check(MapPreferences.Snapshot(new uint[]{5001},GameRankingType.None,5).Count==0,"5v5 custom ignores preferences");
MapPreferences.Set(5001,3,Ballot(7),valid);
Check(MapPreferences.Snapshot(new uint[]{5001},GameRankingType.Friendly,4)[5001].Count(p=>p.Value)==3,"invalid slot cannot modify valid preferences");
using(var reader=new BinaryReader(new MemoryStream(Wire().Concat(Wire(mode:1)).Concat(new byte[]{0,123}).ToArray()))) {
 Check(MapPreferences.TryRead(reader,out byte first,out _)&&first==0&&reader.BaseStream.Position==Wire().Length,"coalesced Casual frame leaves following commands intact");
 Check(MapPreferences.TryRead(reader,out byte second,out _)&&second==1&&reader.ReadByte()==0&&reader.ReadByte()==123,"coalesced Ranked and queue entry stay ordered");
}
for(int i=0;i<Wire().Length;i++)Check(!Read(Wire().Take(i).ToArray()),"truncated payload rejected");
byte[] Frame(byte[] body){using var s=new MemoryStream();using var w=new BinaryWriter(s);w.Write7BitEncodedInt(body.Length);w.Write(body);return s.ToArray();}
var frames=Frame(new byte[]{11,36}.Concat(Wire()).ToArray()).Concat(Frame(new byte[]{11,36}.Concat(Wire(mode:1)).ToArray())).Concat(Frame(new byte[]{11,36}.Concat(Wire(mode:2)).ToArray())).Concat(Frame(new byte[]{11,0,42,0,0,0})).ToArray();
var dispatcher=new PreferenceDispatcher();var session=new BNLReloadedServer.Servers.SessionReader(dispatcher,"fixture");
session.ProcessPacket(frames,0,frames.Length);
Check(dispatcher.Modes.SequenceEqual(new byte[]{0,1,2})&&dispatcher.Queue==42,"real framing routes all three coalesced preferences followed by queue entry");
Console.WriteLine($"PASS {checks} map preference checks");

sealed class PreferenceDispatcher : BNLReloadedServer.Servers.IServiceDispatcher {
 public List<byte> Modes {get;}=[]; public uint Queue {get;private set;}
 public bool Dispatch(BinaryReader reader){if(reader.ReadByte()!=11)return false;byte command=reader.ReadByte();
  if(command==36){if(!MapPreferences.TryRead(reader,out byte mode,out _))return false;Modes.Add(mode);return true;}
  if(command==0){Queue=reader.ReadUInt32();return true;}return false;}
}
