using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ScreensView.Shared.Models;

namespace ScreensView.Viewer.Services;

public class EncryptedComputerStorageService(string filePath, string password) : IComputerStorageService
{
    private const int CurrentVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Pbkdf2Iterations = 100_000;

    private readonly object _fileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<ComputerConfig> Load()
    {
        lock (_fileLock)
        {
            if (!File.Exists(filePath))
                return [];

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            var container = JsonSerializer.Deserialize<EncryptedStorageContainer>(json)
                ?? throw new InvalidDataException("Encrypted computer storage file is invalid.");

            if (container.Version != CurrentVersion)
                throw new InvalidDataException($"Unsupported encrypted storage version: {container.Version}.");

            try
            {
                var salt = Convert.FromBase64String(container.KdfSalt);
                var nonce = Convert.FromBase64String(container.Nonce);
                var tag = Convert.FromBase64String(container.Tag);
                var ciphertext = Convert.FromBase64String(container.Ciphertext);

                using var kdf = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
                var plaintext = new byte[ciphertext.Length];
                using var aes = new AesGcm(kdf.GetBytes(KeySize), TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);

                return JsonSerializer.Deserialize<List<ComputerConfig>>(plaintext) ?? [];
            }
            catch (CryptographicException ex)
            {
                throw new EncryptedComputerStoragePasswordException("Unable to decrypt stored computers with the provided password.", ex);
            }
        }
    }

    public void Save(IEnumerable<ComputerConfig> computers)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(computers, JsonOptions);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var kdf = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        using (var aes = new AesGcm(kdf.GetBytes(KeySize), TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var container = new EncryptedStorageContainer
        {
            Version = CurrentVersion,
            KdfSalt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext)
        };

        var json = JsonSerializer.Serialize(container, JsonOptions);
        lock (_fileLock)
            File.WriteAllText(filePath, json);
    }

    private sealed class EncryptedStorageContainer
    {
        public int Version { get; set; }
        public string KdfSalt { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
    }
}

public class EncryptedComputerStoragePasswordException(string message, Exception? innerException = null)
    : Exception(message, innerException);
