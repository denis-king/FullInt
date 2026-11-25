namespace FullInt.Core
{
    /// <summary>The base a FullInt64 keeps its words in: hex (binary, 64-bit words) or dec (the
    /// decimal mode, 10^19 a word; to be removed with the decimal mode before v1).</summary>
    public enum toBase { dec = 10, hex = 16 }
}
