using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../tools/catalogue/class-perks"));
var cards=Directory.GetFiles(folder,"*.json").Select(p=>File.ReadAllText(p).Deserialize<CardPerk>()!).ToList();
Check(cards.Count==3,"three class perk CDB documents");
foreach(var card in cards){
    ClassPerkCatalogue.Validate(card);
    using var stream=new MemoryStream();
    using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))card.Write(writer);
    stream.Position=0;
    var received=CardPerk.ReadRecord(new BinaryReader(stream));
    Check(stream.Position==stream.Length,"existing wire record consumes all bytes: "+card.Id);
    Check(received.ClassPerk==null && received.SlotType==PerkSlotType.Class,"settings remain server-side: "+card.Id);
    Check(!received.Description!.Text!.Contains('{') && !received.Description2!.Text!.Contains('{') && !received.Label!.Contains('{'),"resolved wire text: "+card.Id);
}
var skill=cards.Single(c=>c.Id==ClassPerkCatalogue.SkillId);
skill.ClassPerk!.RegenMaxHealthPercentPerSecond=7.5f;
Check(ClassPerkCatalogue.Resolve(skill,skill.Label)=="7.5% max health / sec","fractional CDB value formats invariantly");
var catalogue=(ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(cards.Cast<Card>().ToList());
var before=CatalogueBlob.Current;
var invalid=new CardPerk{Id=ClassPerkCatalogue.SkillId,SlotType=PerkSlotType.Class,ClassPerk=new ClassPerkBalance{RegenMaxHealthPercentPerSecond=float.NaN}};
bool rejected=false;
try{catalogue.UpdateCard(invalid);}catch(InvalidDataException){rejected=true;}
Check(rejected && ReferenceEquals(before,CatalogueBlob.Current),"invalid live update preserves catalogue snapshot");
Check(ClassPerkCatalogue.Balance(ClassPerkCatalogue.SkillId).RegenMaxHealthPercentPerSecond==7.5f,"invalid update preserves prior balance");
Console.WriteLine($"CLASS_PERK_CATALOGUE_OK checks={checks}");
