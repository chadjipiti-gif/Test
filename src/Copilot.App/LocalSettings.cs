using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copilot.Core;
namespace Copilot.App;
public sealed class LocalSettings : IKeyStore
{
 public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PrivateConversationCopilot");
 public UserSettings Value { get; private set; } = new();
 public LocalSettings() { Directory.CreateDirectory(Root); try { if(File.Exists(Path.Combine(Root,"settings.json"))) Value=JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(Path.Combine(Root,"settings.json")))??new(); } catch(Exception ex) when(ex is IOException or JsonException) { /* retain corrupt file for local investigation */ } }
 public void Persist() { var p=Path.Combine(Root,"settings.json"); File.WriteAllText(p+".tmp",JsonSerializer.Serialize(Value)); File.Move(p+".tmp",p,true); }
 public string? Get(string provider) { var p=KeyPath(provider); return File.Exists(p)?Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(p),null,DataProtectionScope.CurrentUser)):null; }
 public void Save(string provider,string key) { if(!string.IsNullOrWhiteSpace(key)) File.WriteAllBytes(KeyPath(provider),ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser)); }
 private static string KeyPath(string provider) { if(provider is not ("Deepgram" or "OpenRouter" or "OpenAI")) throw new ArgumentException("Unknown provider"); return Path.Combine(Root,provider+".key"); }
}
