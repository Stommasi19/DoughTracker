using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Infrastructure;

public record TokenReceipt(string AccessToken, string ItemId);

public sealed class TokenSecrets(IDataProtectionProvider protection, IConfiguration config)
{
    private readonly IDataProtector protector = protection.CreateProtector("DoughTracker.PlaidTokens.v1");
    public static string DirectoryPath(IConfiguration config) => config["Secrets:Directory"] ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "doughtracker-secrets");
    public static string Reference(string owner, string publicToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner + ":" + publicToken))).ToLowerInvariant();
    private string FilePath(string reference)
    {
        if (reference.Length != 64 || reference.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidOperationException("Invalid secret reference.");
        return Path.Combine(DirectoryPath(config), reference + ".secret");
    }
    public async Task<TokenReceipt?> Read(string reference, CancellationToken ct)
    {
        var file = FilePath(reference);
        if (!File.Exists(file)) return null;
        return JsonSerializer.Deserialize<TokenReceipt>(protector.Unprotect(await File.ReadAllTextAsync(file, ct)));
    }
    public async Task Write(string reference, TokenReceipt receipt, CancellationToken ct)
    {
        var directory = DirectoryPath(config);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporary = Path.Combine(directory, Guid.NewGuid() + ".tmp");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            await using (var file = new FileStream(temporary, options))
            await using (var writer = new StreamWriter(file))
            {
                await writer.WriteAsync(protector.Protect(JsonSerializer.Serialize(receipt)).AsMemory(), ct);
                await writer.FlushAsync(ct);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath(reference), overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
    public void Delete(string reference) => File.Delete(FilePath(reference));
}
