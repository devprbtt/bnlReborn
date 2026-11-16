using BNLReloadedServer.Database;
using SQLite;

var path=Path.Combine(Path.GetTempPath(), "bnl-player-search-"+Guid.NewGuid()+".db");
int checks=0;
void Check(bool ok,string label) { if(!ok)throw new Exception(label); Console.WriteLine("PASS "+label); checks++; }
try {
    var db=new SQLiteAsyncConnection(path);
    await db.CreateTableAsync<PlayerRecord>();
    string[] names=["[Clan] tReX | Xavier","TrexFan","TREX","OtherPlayer","TRÉX","100%Real","under_score","O'Neil","path\\name"];
    for(int i=0;i<names.Length;i++)await db.InsertAsync(new PlayerRecord { PlayerId=(uint)i+1, SteamId=(ulong)i+100, Username=names[i] });
    var results=await PlayerNameSearch.Read(db,"trex");
    Check(results.Select(p=>p.PlayerId).SequenceEqual(new uint[]{3,2,1}),"exact, prefix, decorated substring ranking");
    Check((await PlayerNameSearch.Read(db,"TrEx")).Select(p=>p.PlayerId).SequenceEqual(results.Select(p=>p.PlayerId)),"mixed capitalization gives identical results");
    Check((await PlayerNameSearch.Read(db,"  trex  ")).Count==3,"outer whitespace ignored");
    Check((await PlayerNameSearch.Read(db,"xavier")).Single().PlayerId==1,"nickname suffix finds decorated Trex account");
    Check((await PlayerNameSearch.Read(db,"réx")).Single().PlayerId==5,"Unicode case-insensitive partial name");
    Check((await PlayerNameSearch.Read(db,"%")).Single().PlayerId==6,"percent is literal");
    Check((await PlayerNameSearch.Read(db,"_")).Single().PlayerId==7,"underscore is literal");
    Check((await PlayerNameSearch.Read(db,"o'neil")).Single().PlayerId==8,"apostrophe is literal");
    Check((await PlayerNameSearch.Read(db,"\\")).Single().PlayerId==9,"backslash is literal");
    Check((await PlayerNameSearch.Read(db,"' OR 1=1 --")).Count==0,"SQL-like input cannot broaden search");
    Check((await PlayerNameSearch.Read(db," ")).Count==0&&(await PlayerNameSearch.Read(db,null)).Count==0,"blank and null queries return nothing");
    Check(results.Last().Nickname==names[0]&&results.Last().SteamId==100,"result identity and original nickname preserved");
    for(int i=0;i<60;i++)await db.InsertAsync(new PlayerRecord { PlayerId=(uint)i+100,Username="Match"+i.ToString("D2") });
    var many=await PlayerNameSearch.Read(db,"match");
    Check(many.Count==50&&many.Last().Nickname=="Match49","result limit and stable alphabetical ordering");
    Check((await PlayerNameSearch.Read(db,"no such account")).Count==0,"missing account returns nothing");
    Console.WriteLine($"PASS {checks} search checks");
} finally { SQLiteAsyncConnection.ResetPool(); if(File.Exists(path))File.Delete(path); }
