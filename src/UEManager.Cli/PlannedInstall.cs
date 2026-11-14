using UEManager.Core.Mods;

namespace UEManager.Cli;

/// <summary>
/// Builds the read-only install plan that `plan` prints and `install` follows: the same Inspect and Check the app's
/// install review runs, with the parts it would tick first. Nothing here changes the game.
/// </summary>
internal sealed class PlannedInstall : IDisposable
{
    private PlannedInstall(ModManager manager, InstallPreview preview, IReadOnlyList<PackageGroup> selected, InstallCheck check,
        PlanPayload plan)
    {
        Manager = manager;
        Preview = preview;
        Selected = selected;
        Check = check;
        Plan = plan;
    }

    /// <summary>The manager that read and checked the download; <see cref="InstallCommand"/> installs with it.</summary>
    public ModManager Manager { get; }

    public InstallPreview Preview { get; }

    public IReadOnlyList<PackageGroup> Selected { get; }

    public InstallCheck Check { get; }

    public PlanPayload Plan { get; }

    public void Dispose() => Preview.Dispose();

    /// <exception cref="ModException">The download cannot be read.</exception>
    public static PlannedInstall Build(CliContext context, string source, bool allParts)
    {
        var manager = context.OpenManager();
        var preview = manager.Inspect(source);
        try
        {
            // The parts the app's install review ticks first, by the same rules in Core.
            var selected = allParts ? preview.Packages.ToList() : manager.FirstSelection(preview).ToList();
            var check = manager.Check(preview, selected);
            // An update keeps the variant that is installed now, as the review does.
            if (!allParts && KeepInstalledVariant(preview, selected, check))
                check = manager.Check(preview, selected);
            return new PlannedInstall(manager, preview, selected, check, BuildPlan(context, manager, preview, selected, check, allParts));
        }
        catch
        {
            preview.Dispose();
            throw;
        }
    }

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
        IReadOnlyList<PackageGroup> selected, InstallCheck check, bool allParts)
    {
        var parts = preview.Packages.Select((part, index) => new PlanPartDto(index, part.Name, part.Folder, part.Label,
            part.Kind.ToString(), part.Target.ToString(), part.TypeLabel, part.Size, part.Files.Count, selected.Contains(part),
            part.IsUncertain, part.IsAlternate, part.Variant, part.VariantOf, part.NeedsLoader,
            [.. part.Problems.Select(Output.Text)], [.. part.Warnings.Select(Output.Text)], part.RelativePaths)).ToList();

        var entries = new List<PlanEntryDto>();
        foreach (var part in selected)
        {
            for (var i = 0; i < part.Files.Count; i++)
            {
                var target = part.Target;
                var relative = part.RelativePaths[i];
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
            parts, entries, existing, gameFiles, replaces, check.EditedSettings ?? [], check.NeedsUe4ss, check.CanInstall,
            [.. check.Problems.Select(Output.Text)], [.. check.Warnings.Select(Output.Text)],
            new PlanIgnoredDto(preview.IgnoredFileCount, preview.LeftOutFiles.Count, preview.IgnoredFiles, preview.LeftOutFiles,
                preview.NestedArchiveCount, preview.ContainsUe4ssRuntime));
    }

    /// <summary>The installed mod whose file is at this place, if any; null for the game's own file or none.</summary>
    private static string? Owner(ModManager manager, DeployTarget target, string relativePath) =>
        manager.Mods.FirstOrDefault(mod => mod.Files.Any(file => file.Target == target
            && file.FileName.Equals(relativePath, StringComparison.OrdinalIgnoreCase)))?.Name;
}
