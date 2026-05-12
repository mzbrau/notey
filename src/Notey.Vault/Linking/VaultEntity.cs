namespace Notey.Vault.Linking;

public sealed record VaultEntity(
    VaultEntityKind Kind,
    string Name,
    string FilePath,
    string LinkPath,
    IReadOnlyList<string> Aliases)
{
    public string ToWikiLink()
    {
        return ObsidianLinkBuilder.FormatWikiLink(LinkPath, Name);
    }
}
