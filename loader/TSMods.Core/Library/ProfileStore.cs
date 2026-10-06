using System.Text.Json;

namespace TSMods.Core.Library;

public sealed record ProfileMod(string Guid, string EntryId);

public sealed record Profile(string Name, IReadOnlyList<ProfileMod> Mods);

public sealed class ProfileStore(string root)
{
    public IReadOnlyList<Profile> All()
    {
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.json")
            .Select(path => JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Json.Options))
            .OfType<Profile>()
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Profile? Find(string name) =>
        All().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Save(Profile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profile needs a name.");
        Directory.CreateDirectory(root);
        File.WriteAllText(PathFor(profile.Name), JsonSerializer.Serialize(profile, Json.Options));
    }

    public bool Delete(string name)
    {
        var profile = Find(name);
        if (profile is null) return false;
        File.Delete(PathFor(profile.Name));
        return true;
    }

    private string PathFor(string name) =>
        Path.Combine(root, string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");
}
