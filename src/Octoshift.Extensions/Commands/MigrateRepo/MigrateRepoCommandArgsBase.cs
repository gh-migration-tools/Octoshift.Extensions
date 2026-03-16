using OctoshiftCLI.Extensions;

namespace OctoshiftCLI.Commands.MigrateRepo;

public abstract class MigrateRepoCommandArgsBase : CommandArgs
{
    [Secret]
    public string? GithubPat { get; set; } = null!;
    public string GithubOrg { get; set; } = null!;
    public string GithubRepo { get; set; } = null!;
    public bool QueueOnly { get; set; }
    public string? TargetRepoVisibility { get; set; }
    public string? TargetApiUrl { get; set; }
    public string? TargetUploadsUrl { get; set; }
    public string? ArchiveUrl { get; set; } = null!;
    public string? ArchivePath { get; set; } = null!;
    public bool KeepArchive { get; set; }

    public abstract bool ShouldGenerateArchive();

    public virtual bool ShouldImportArchive() =>
        ArchiveUrl.HasValue() || GithubOrg.HasValue();

    public virtual bool ShouldUploadArchive() =>
        ArchiveUrl.IsNullOrWhiteSpace() && GithubOrg.HasValue();
}
