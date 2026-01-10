using System;

namespace DocMaster.ErasureCoding;

/// <summary>
/// Reed-Solomon erasure coder interface for encoding and decoding data.
/// </summary>
public interface IErasureCoder : IDisposable
{
    /// <summary>
    /// Encodes data into k data shards and m parity shards.
    /// </summary>
    /// <param name="data">Input data to encode. Maximum size: 1GB.</param>
    /// <returns>
    /// Array of shards: [data_shard_0...data_shard_k-1, parity_shard_0...parity_shard_m-1]
    /// All shards are the same size.
    /// </returns>
    /// <exception cref="ArgumentNullException">data is null</exception>
    /// <exception cref="ArgumentException">data is empty or too large</exception>
    /// <exception cref="ObjectDisposedException">Coder has been disposed</exception>
    /// <example>
    /// <code>
    /// using var coder = new IsaLErasureCoder(dataShards: 6, parityShards: 3);
    /// byte[] data = File.ReadAllBytes("myfile.dat");
    /// byte[][] shards = coder.Encode(data);
    /// // shards.Length == 9 (6 data + 3 parity)
    /// </code>
    /// </example>
    byte[][] Encode(byte[] data);

    /// <summary>
    /// Decodes original data from available shards. Requires at least k shards.
    /// </summary>
    /// <param name="shards">
    /// Array of shards (data and parity). Missing shards should be null.
    /// Length must equal k + m.
    /// </param>
    /// <param name="shardPresent">
    /// Boolean array indicating which shards are available.
    /// Length must equal k + m.
    /// </param>
    /// <param name="originalSize">Original data size before encoding (to remove padding)</param>
    /// <returns>Decoded original data</returns>
    /// <exception cref="ArgumentNullException">shards or shardPresent is null</exception>
    /// <exception cref="ArgumentException">Insufficient shards (need at least k)</exception>
    /// <exception cref="ObjectDisposedException">Coder has been disposed</exception>
    /// <example>
    /// <code>
    /// // Even with 3 shards missing, can still decode
    /// bool[] present = { true, true, false, true, true, false, true, false, true };
    /// byte[] recovered = coder.Decode(shards, present, originalSize: 12345);
    /// </code>
    /// </example>
    byte[] Decode(byte[][] shards, bool[] shardPresent, int originalSize);

    /// <summary>Number of data shards (k)</summary>
    int DataShards { get; }

    /// <summary>Number of parity shards (m)</summary>
    int ParityShards { get; }

    /// <summary>Total shards (k + m)</summary>
    int TotalShards { get; }
}
