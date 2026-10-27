using BNLReloadedServer.Database;
using BNLReloadedServer.BaseTypes;
int checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
// Dummy construction requires unrelated XP catalogue fixtures; wallet reads have no state.
foreach(IPlayerDatabase db in new IPlayerDatabase[]{new PlayerDatabase(),(DummyPlayerDatabase)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DummyPlayerDatabase))})
{
 var first=db.GetCurrency(1);var second=db.GetCurrency(2);
 Check(first.Count==2 && first[CurrencyType.Virtual]==0 && first[CurrencyType.Real]==0,db.GetType().Name+" starts both currencies at zero");
 Check(second[CurrencyType.Virtual]==0 && second[CurrencyType.Real]==0,"other player also starts at zero");
 first[CurrencyType.Virtual]=123;first[CurrencyType.Real]=456;
 Check(second[CurrencyType.Virtual]==0 && second[CurrencyType.Real]==0,"one snapshot cannot alter another player");
 var again=db.GetCurrency(1);
 Check(again[CurrencyType.Virtual]==0 && again[CurrencyType.Real]==0,"snapshot mutation cannot inject test currency");
}
Console.WriteLine($"PASS currency fixture: {checks} checks");
