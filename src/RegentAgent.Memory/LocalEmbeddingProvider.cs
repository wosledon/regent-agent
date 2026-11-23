using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RegentAgent.Memory;

public sealed partial class LocalEmbeddingProvider
{
    public const string ModelVersion = "local-hash-v1";
    private const int Dimensions = 256;

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    public static float[] Embed(string content)
    {
        var vector = new float[Dimensions];
        foreach (var token in Tokenize(content))
        {
            var (index, sign) = HashToken(token);
            vector[index] += sign;
        }
        var norm = Math.Sqrt(vector.Sum(value => value * value));
        if (norm <= 0) return vector;
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
        return vector;
    }

    public static double Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length) throw new ArgumentException("Vector dimensions do not match.");
        return left.Zip(right).Sum(pair => pair.First * pair.Second);
    }

    public static string ContentHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Normalize(NormalizationForm.FormC)))).ToLowerInvariant();

    public static string[] Tokenize(string content)
    {
        var normalized = content.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var tokens = new List<string>();
        foreach (Match match in TokenPattern().Matches(normalized))
        {
            var token = match.Value;
            tokens.Add(token);
            if (token.Length > 2)
            {
                for (var i = 0; i < token.Length - 1; i++)
                    tokens.Add(token.Substring(i, 2));
            }
        }
        return tokens.ToArray();
    }

    private static (int Index, int Sign) HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var index = BitConverter.ToInt32(hash, 0) & int.MaxValue;
        return (index % Dimensions, hash[4] % 2 == 0 ? 1 : -1);
    }
}
