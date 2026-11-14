using System.Globalization;
using UEManager.Core.Mods;

namespace UEManager.Cli;

/// <summary>
/// Builds the read-only install plan that `plan` prints and `install` follows: the same Inspect and Check the app's
/// install review runs, with the parts it would tick first. Nothing here changes the game.
/// </summary>
internal sealed class PlannedInstall : IDisposable
{
    private PlannedInstall(ModManager manager, InstallPreview preview, IReadOnlyList<PackageGroup> selected, InstallCheck check,
        string? ownFolder, PlanPayload plan)
    {
        Manager = manager;
        Preview = preview;
        Selected = selected;
        Check = check;
        OwnFolder = ownFolder;
        Plan = plan;
    }

    /// <summary>The manager that read and checked the download; <see cref="InstallCommand"/> installs with it.</summary>
    public ModManager Manager { get; }

    public InstallPreview Preview { get; }

    public IReadOnlyList<PackageGroup> Selected { get; }

    public InstallCheck Check { get; }

    /// <summary>
    /// The folder below ~mods for a download kept beside an installed mod instead of replacing it; null for an install
    /// that takes its usual places. It goes to <see cref="ModManager.Install"/> as <c>ownFolder</c>.
    /// </summary>
    public string? OwnFolder { get; }

    public PlanPayload Plan { get; }

    public void Dispose() => Preview.Dispose();

    /// <exception cref="CliError">The selection is invalid, such as an unknown --part.</exception>
    /// <exception cref="ModException">The download cannot be read.</exception>
    public static PlannedInstall Build(CliContext context, CliArguments arguments, string source)
    {
        if (arguments.AllParts && arguments.Parts.Count > 0)
            throw new CliError("invalid-arguments", "Options '--all-parts' and '--part' cannot be used together.",
                details: ["Use --all-parts for every part, or --part for the parts you name."]);
        var manager = context.OpenManager();
        var preview = manager.Inspect(source, StagingFolder(arguments.Staging));
        try
        {
            // The parts the app's install review ticks first, by the same rules in Core; --part takes exactly those named.
            var selected = arguments.Parts.Count > 0
                ? Parts(preview, arguments.Parts).ToList()
                : arguments.AllParts ? preview.Packages.ToList() : manager.FirstSelection(preview).ToList();
            var check = manager.Check(preview, selected);
            // An update keeps the variant that is installed now, as the review does. A part the player named stays.
            if (arguments.Parts.Count == 0 && !arguments.AllParts && KeepInstalledVariant(preview, selected, check))
                check = manager.Check(preview, selected);
            // --keep-both: the installed mod the download would replace stays, and the download goes into a folder of its
            // own below ~mods, as the review's "Keep both" does (see InstallDialogViewModel.OwnFolder).
            string? ownFolder = null;
            if (arguments.KeepBoth && check.Replaces is not null)
            {
                ownFolder = manager.NewModFolder(preview.SuggestedName);
                check = manager.Check(preview, selected, null, ownFolder);
            }
            return new PlannedInstall(manager, preview, selected, check, ownFolder,
                BuildPlan(context, manager, preview, selected, check, arguments.AllParts, ownFolder));
        }
        catch
        {
            preview.Dispose();
            throw;
        }
    }

    /// <summary>The --staging folder as a full path; null keeps the data folder's staging folder.</summary>
    /// <exception cref="CliError">The option names no folder.</exception>
    private static string? StagingFolder(string? given)
    {
        if (given is null)
            return null;
        if (string.IsNullOrWhiteSpace(given))
            throw new CliError("invalid-arguments", "Option '--staging' needs a folder.");
        var full = Path.GetFullPath(given);
        if (File.Exists(full))
            throw new CliError("invalid-arguments", $"Option '--staging' needs a folder, but '{full}' is a file.");
        return full;
    }

    /// <summary>The parts named by --part, by their index in the plan (1-based) or their name or folder/name.</summary>
    /// <exception cref="CliError">One of the names matches no part; the details list the available parts.</exception>
    private static IReadOnlyList<PackageGroup> Parts(InstallPreview preview, IReadOnlyList<string> requested)
    {
        var selected = new List<PackageGroup>();
        foreach (var request in requested)
        {
            var spec = request.Trim();
            if (spec.Length == 0)
                throw new CliError("invalid-arguments", "Option '--part' needs a part's index or name.",
                    details: AvailableParts(preview));
            var part = int.TryParse(spec, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                ? index >= 1 && index <= preview.Packages.Count ? preview.Packages[index - 1] : null
                : preview.Packages.FirstOrDefault(p => p.Label.Equals(spec, StringComparison.OrdinalIgnoreCase)
                    || p.Name.Equals(spec, StringComparison.OrdinalIgnoreCase));
            if (part is null)
                throw new CliError("invalid-arguments", $"No part matches '{spec}'.", details: AvailableParts(preview));
            if (!selected.Contains(part))
                selected.Add(part);
        }
        return selected;
    }

    /// <summary>The parts a --part name may mean, for the error message: their index in the plan and where they go.</summary>
    private static string[] AvailableParts(InstallPreview preview) =>
    [
        "Available parts:",
        .. preview.Packages.Select((part, index) => $"  {index + 1}: {part.Label} ({part.Target})"),
    ];

    /// <summary>
    /// Selects the variant of the mod the download updates (see <see cref="ModManager.InstalledVariant"/>), in place of
    /// the other variants of its choice, so an update keeps the variant the player chose.
    /// </summary>
    private static bool KeepInstalledVariant(InstallPreview preview, List<PackageGroup> selected, InstallCheck check)
    {
        if (ModManager.InstalledVariant(preview, check) is not { } same || selected.Contains(same))
            return false;
        selected.RemoveAll(part => part != same && part.VariantOf == same.VariantOf);
        selected.Add(same);
        return true;
    }

    private static PlanPayload BuildPlan(CliContext context, ModManager manager, InstallPreview preview,
        IReadOnlyList<PackageGroup> selected, InstallCheck check, bool allParts, string? ownFolder)
    {
        var parts = preview.Packages.Select((part, index) => new PlanPartDto(index + 1, part.Name, part.Folder, part.Label,
            part.Kind.ToString(), part.Target.ToString(), part.TypeLabel, part.Size, part.Files.Count, selected.Contains(part),
            part.IsUncertain, part.IsAlternate, part.Variant, part.VariantOf, part.NeedsLoader,
            [.. part.Problems.Select(Output.Text)], [.. part.Warnings.Select(Output.Text)], part.RelativePaths)).ToList();

        var entries = new List<PlanEntryDto>();
        foreach (var part in selected)
        {
            for (var i = 0; i < part.Files.Count; i++)
            {
                var target = part.Target;
                // A kept mod's files go into its folder below ~mods, as they will be installed.
                var relative = ModManager.InFolder(ownFolder, target, part.RelativePaths[i]);
                var path = Path.Combine(manager.TargetFolder(target), relative);
                entries.Add(new PlanEntryDto(target.ToString(), relative, path, File.Exists(path), Owner(manager, target, relative)));
            }
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        var existing = entries.Where(entry => entry.Exists).ToList();
        var gameFiles = existing.Where(entry => entry.Target is nameof(DeployTarget.InstallRoot) or nameof(DeployTarget.ExeFolder)).ToList();
        var replaces = check.Replaces is { } old
            ? new PlanReplacedDto(old.Id, old.Name, old.Version, old.Enabled, old.Variant)
            : null;

        return new PlanPayload(1, "plan", preview.Source, preview.SourceFileName, preview.SuggestedName, preview.SuggestedVersion,
            context.GameId, Mapping.Game(context.Profile, context.Game, checkRunning: false),
            new PlanSelectionDto(allParts, preview.Packages.Count, selected.Count,
                [.. selected.Select(part => part.Label)]),
            parts, entries, existing, gameFiles, replaces, ownFolder is not null, ownFolder, check.EditedSettings ?? [],
            check.NeedsUe4ss, check.CanInstall,
            [.. check.Problems.Select(Output.Text)], [.. check.Warnings.Select(Output.Text)],
            new PlanIgnoredDto(preview.IgnoredFileCount, preview.LeftOutFiles.Count, preview.IgnoredFiles, preview.LeftOutFiles,
                preview.NestedArchiveCount, preview.ContainsUe4ssRuntime));
    }

    /// <summary>The installed mod whose file is at this place, if any; null for the game's own file or none.</summary>
    private static string? Owner(ModManager manager, DeployTarget target, string relativePath) =>
        manager.Mods.FirstOrDefault(mod => mod.Files.Any(file => file.Target == target
            && file.FileName.Equals(relativePath, StringComparison.OrdinalIgnoreCase)))?.Name;
}
