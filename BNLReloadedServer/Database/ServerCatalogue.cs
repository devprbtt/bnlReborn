using System.Collections.Frozen;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;

namespace BNLReloadedServer.Database;

public class ServerCatalogue : Catalogue
{
    private sealed class Snapshot
    {
        public readonly FrozenDictionary<Key, Card> Cards;
        public readonly CardBlock?[] Blocks = new CardBlock?[65536];
        public Snapshot(FrozenDictionary<Key, Card> cards)
        {
            Cards = cards;
            foreach (var block in cards.Values.OfType<CardBlock>()) Blocks[block.BlockId] = block;
        }
    }

    private volatile Snapshot _snapshot;
    private FrozenDictionary<Key, Card> _db => _snapshot.Cards;

    // Publish keyed cards and the block-id index together; readers never use a process-lifetime cache.
    public CardBlock? GetBlockCard(ushort id)
    {
        var snapshot = _snapshot;
        return snapshot.Blocks[id];
    }
    private readonly Lock _updateLock = new();

    public ServerCatalogue()
    {
        _snapshot = new Snapshot(FrozenDictionary<Key, Card>.Empty);
    }

    public override Card? GetCard(Key key)
    {
        return _db.GetValueOrDefault(key);
    }

    public override IEnumerable<Card> All => _db.Values;

    public void Replicate(List<Card> cards)
    {
        lock (_updateLock)
        {
            ConquestMapRegistration.Register(cards);
            var tempDict = new Dictionary<Key, Card>(KeyEqualityComparer.Instance);
            foreach (var card in cards)
            {
                if (card.Id == null) continue;
                card.Key = Key(card.Id);
                tempDict.Add(card.Key, card);
            }
            _snapshot = new Snapshot(tempDict.ToFrozenDictionary());
            Replicated = true;
            CatalogueBlob.Set(cards);
        }
    }

    public void UpdateCard(Card card)
    {
        if (card.Id == null) return;
        lock (_updateLock)
        {
            card.Key = Key(card.Id);
            var cards = _db.Values.Where(existing => existing.Id != card.Id).Append(card).ToList();
            ValidateIncrementalChange(cards);

            var tempDict = new Dictionary<Key, Card>(_db, KeyEqualityComparer.Instance)
            {
                [card.Key] = card
            };
            _snapshot = new Snapshot(tempDict.ToFrozenDictionary());
            CatalogueBlob.Set(cards);
        }
    }

    public bool RemoveCard(string id)
    {
        lock (_updateLock)
        {
            var key = Key(id);
            if (!_db.TryGetValue(key, out var existing) || existing.Id != id) return false;

            var cards = _db.Values.Where(card => card.Id != id).ToList();
            ValidateIncrementalChange(cards);

            var tempDict = new Dictionary<Key, Card>(_db, KeyEqualityComparer.Instance);
            tempDict.Remove(key);
            _snapshot = new Snapshot(tempDict.ToFrozenDictionary());
            CatalogueBlob.Set(cards);
            return true;
        }
    }

    private static void ValidateIncrementalChange(List<Card> cards)
    {
        var problems = CatalogueValidator.Validate(cards);
        if (problems.Count > 0)
            throw new InvalidOperationException($"Rejected incremental catalogue change — {string.Join("; ", problems)}");
    }
}
