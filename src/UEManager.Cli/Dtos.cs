namespace UEManager.Cli;

/// <summary>A file of an installed mod, as mods.json records it.</summary>
internal sealed record ModFileDto(string FileName, string Target, long Size, string Sha256, string? DeployedPath, bool Deployed,
    bool PlayerFile, bool Shared, bool GameFile, bool Made, bool Borrowed);

/// <summary>An installed mod. The field names are the ones the CLI documents, so scripts can rely on them.</summary>
internal sealed record ModDto(string Id, string Name, string? Version, bool Enabled, string Type, int FileCount, long TotalSize,
    DateTimeOffset InstalledAt, string? SourceFileName, string? CatalogId, bool Recommended, IReadOnlyList<string> Tags, bool NotTracked,
    bool NeedsUe4ss, string Integrity, IReadOnlyList<string> Targets, IReadOnlyList<ModFileDto> Files);

internal sealed record StatusPayload(int SchemaVersion, string Command, ToolInfo Tool, string DataDir, string SettingsFile,
    bool SettingsExists, AppDto? App, GameInfo Game, ModsCountDto Mods, IReadOnlyList<string> Warnings);

internal sealed record ListPayload(int SchemaVersion, string Command, string DataDir, string GameId, int Count, IReadOnlyList<ModDto> Mods);

/// <summary>One deployed file checked against the manager's recorded SHA-256.</summary>
internal sealed record VerifyFileDto(string ModId, string ModName, string FileName, string Target, string? DeployedPath,
    string ExpectedSha256, string Status, string? ActualSha256, long? ActualSize, bool ExpectedChange, string? Error);

internal sealed record VerifyModDto(string Id, string Name, bool Enabled, string Status, int FileCount, IReadOnlyList<VerifyFileDto> Files);

internal sealed record VerifySummaryDto(int Mods, int FilesChecked, int FilesOk, int FilesChanged, int FilesMissing, int FilesUnreadable,
    int ExpectedChanges, int Problems);

internal sealed record VerifyPayload(int SchemaVersion, string Command, string DataDir, string GameId, VerifySummaryDto Summary,
    IReadOnlyList<VerifyModDto> Mods, IReadOnlyList<VerifyFileDto> Problems, IReadOnlyList<string> Warnings);

/// <summary>One installable part, as the install review would list it, with whether the plan selects it.</summary>
internal sealed record PlanPartDto(int Index, string Name, string Folder, string Label, string Kind, string Target, string Type,
    long Size, int FileCount, bool Selected, bool Uncertain, bool Alternate, string? Variant, string? VariantOf, string? NeedsLoader,
    IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings, IReadOnlyList<string> Files);

/// <summary>A file the planned parts place in the game, and what is there now.</summary>
internal sealed record PlanEntryDto(string Target, string RelativePath, string Path, bool Exists, string? Owner);

/// <summary>The installed mod the download replaces, when it updates one.</summary>
internal sealed record PlanReplacedDto(string Id, string Name, string? Version, bool Enabled, string? Variant);

internal sealed record PlanIgnoredDto(int Count, int LeftOutCount, IReadOnlyList<string> Files, IReadOnlyList<string> LeftOut,
    int NestedArchiveCount, bool ContainsUe4ssRuntime);

internal sealed record PlanSelectionDto(bool AllParts, int PartCount, int SelectedCount, IReadOnlyList<string> SelectedLabels);

/// <summary>The read-only install plan: the same Check the install review runs, without touching the game.</summary>
internal sealed record PlanPayload(int SchemaVersion, string Command, string Source, string SourceFileName, string SuggestedName,
    string? SuggestedVersion, string GameId, GameInfo Game, PlanSelectionDto Selection, IReadOnlyList<PlanPartDto> Parts,
    IReadOnlyList<PlanEntryDto> Entries, IReadOnlyList<PlanEntryDto> ExistingFiles, IReadOnlyList<PlanEntryDto> GameFilesReplaced,
    PlanReplacedDto? Replaces, IReadOnlyList<string> EditedSettings, bool NeedsUe4ss, bool CanInstall, IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings, PlanIgnoredDto Ignored);

internal sealed record InstallPayload(int SchemaVersion, string Command, string Source, ModDto Installed, PlanReplacedDto? Replaced,
    IReadOnlyList<string> Warnings);

internal sealed record ToggleModDto(string Id, string Name, bool EnabledBefore, bool EnabledAfter, bool Changed, int DeployedFiles,
    string Integrity);

internal sealed record TogglePayload(int SchemaVersion, string Command, string GameId, ToggleModDto Mod);

internal sealed record UninstallModDto(string Id, string Name, string? Version, bool Enabled, int FileCount, long TotalSize,
    string LibraryFolder, IReadOnlyList<ModFileDto> Files);

internal sealed record UninstallPayload(int SchemaVersion, string Command, string GameId, bool Confirmed, string? BackupFolder,
    int RemainingMods, UninstallModDto Mod);
