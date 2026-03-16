using OctoshiftCLI.Services;

namespace OctoshiftCLI.Commands.MigrateRepo;

public abstract class MigrateRepoCommandHandlerBase<TArgs> :
    ICommandHandler<TArgs>
    where TArgs : MigrateRepoCommandArgsBase
{
    private const int CheckMigrationStatusDelayInMilliseconds = 60000;

    private readonly OctoLogger _log;
    private readonly GithubApi _githubApi;
    private readonly EnvironmentVariableProvider _environmentVariableProvider;
    private readonly FileSystemProvider _fileSystemProvider;
    private readonly WarningsCountLogger _warningsCountLogger;

    protected MigrateRepoCommandHandlerBase(
        OctoLogger log,
        GithubApi githubApi,
        EnvironmentVariableProvider environmentVariableProvider,
        FileSystemProvider fileSystemProvider,
        WarningsCountLogger warningsCountLogger)
    {
        _log = log;
        _githubApi = githubApi;
        _environmentVariableProvider = environmentVariableProvider;
        _fileSystemProvider = fileSystemProvider;
        _warningsCountLogger = warningsCountLogger;
    }

    public async Task Handle(TArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        await ValidateOptionsAsync(args).ConfigureAwait(false);

        var migrationSourceId = string.Empty;
        var archiveFilePath = string.Empty;

        if (args.ShouldImportArchive())
        {
            var targetRepoExists = await _githubApi.DoesRepoExist(args.GithubOrg, args.GithubRepo).ConfigureAwait(false);

            if (targetRepoExists)
            {
                throw new OctoshiftCliException($"A repository called {args.GithubOrg}/{args.GithubRepo} already exists");
            }

            migrationSourceId = await CreateMigrationSource(args).ConfigureAwait(false);
        }

        if (args.ShouldGenerateArchive())
        {
            archiveFilePath = await GenerateArchiveAsync(args).ConfigureAwait(false);
        }

        if (args.ShouldUploadArchive())
        {
            try
            {
                args.ArchiveUrl = await UploadArchiveToGitHub(args.GithubOrg, archiveFilePath).ConfigureAwait(false);
            }
            finally
            {
                if (!args.KeepArchive)
                {
                    DeleteArchive(args.ArchivePath!);
                }
            }
        }

        if (args.ShouldImportArchive())
        {
            await ImportArchive(args, migrationSourceId, args.ArchiveUrl).ConfigureAwait(false);
        }
    }

    protected abstract Task ValidateOptionsAsync(TArgs args);

    protected abstract Task<string> GenerateArchiveAsync(TArgs args);

    protected abstract string GetRepoUrl(TArgs args);

    protected virtual Task<string> CreateMigrationSource(string orgId) =>
        _githubApi.CreateGhecMigrationSource(orgId);

    protected virtual Task<string> StartMigration(
        string migrationSourceId,
        string repoUrl,
        string orgId,
        string repo,
        string token,
        string? archiveUrl,
        string? targetRepoVisibility = null) =>
        _githubApi.StartMigration(
            migrationSourceId,
            repoUrl,
            orgId,
            repo,
            sourceToken: token,
            targetToken: token,
            gitArchiveUrl: archiveUrl,
            metadataArchiveUrl: archiveUrl,
            targetRepoVisibility: targetRepoVisibility);

    protected virtual Task<(string State, string RepositoryName, int WarningsCount, string FailureReason, string MigrationLogUrl)> GetMigration(string migrationId) =>
        _githubApi.GetMigration(migrationId);

    private async Task<string> CreateMigrationSource(TArgs args)
    {
        _log.LogInformation("Creating Migration Source...");

        args.GithubPat ??= _environmentVariableProvider.TargetGithubPersonalAccessToken();
        var orgId = await _githubApi.GetOrganizationId(args.GithubOrg).ConfigureAwait(false);

        try
        {
            return await CreateMigrationSource(orgId).ConfigureAwait(false);
        }
        catch (OctoshiftCliException ex) when (ex.Message.Contains("not have the correct permissions to execute", StringComparison.OrdinalIgnoreCase))
        {
            var insufficientPermissionsMessage = InsufficientPermissionsMessageGenerator.Generate(args.GithubOrg);
            var message = $"{ex.Message}{insufficientPermissionsMessage}";
            throw new OctoshiftCliException(message, ex);
        }
    }

    private void DeleteArchive(string path)
    {
        try
        {
            _fileSystemProvider.DeleteIfExists(path);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Couldn't delete the archive. Error message: \"{ex.Message}\"");
            _log.LogVerbose(ex.ToString());
        }
    }

    private async Task ImportArchive(TArgs args, string migrationSourceId, string? archiveUrl = null)
    {
        _log.LogInformation("Importing Archive...");

        archiveUrl ??= args.ArchiveUrl;

        var repoUrl = GetRepoUrl(args);

        args.GithubPat ??= _environmentVariableProvider.TargetGithubPersonalAccessToken();
        var githubOrgId = await _githubApi.GetOrganizationId(args.GithubOrg).ConfigureAwait(false);

        string migrationId;

        try
        {
            args.TargetRepoVisibility ??= "private";

            migrationId = await StartMigration(migrationSourceId, repoUrl, githubOrgId, args.GithubRepo, args.GithubPat, archiveUrl, args.TargetRepoVisibility).ConfigureAwait(false);
        }
        catch (OctoshiftCliException ex) when (ex.Message == $"A repository called {args.GithubOrg}/{args.GithubRepo} already exists")
        {
            _log.LogWarning($"The Org '{args.GithubOrg}' already contains a repository with the name '{args.GithubRepo}'. No operation will be performed");
            return;
        }

        if (args.QueueOnly)
        {
            _log.LogInformation($"A repository migration (ID: {migrationId}) was successfully queued.");
            return;
        }

        var (migrationState, _, warningsCount, failureReason, migrationLogUrl) = await _githubApi.GetMigration(migrationId).ConfigureAwait(false);

        while (RepositoryMigrationStatus.IsPending(migrationState))
        {
            _log.LogInformation($"Migration in progress (ID: {migrationId}). State: {migrationState}. Waiting 60 seconds...");
            await Task.Delay(CheckMigrationStatusDelayInMilliseconds).ConfigureAwait(false);
            (migrationState, _, warningsCount, failureReason, migrationLogUrl) = await GetMigration(migrationId).ConfigureAwait(false);
        }

        var migrationLogAvailableMessage = $"Migration log available at {migrationLogUrl} or by running `gh {CliContext.RootCommand} download-logs --github-org {args.GithubOrg} --github-repo {args.GithubRepo}`";

        if (RepositoryMigrationStatus.IsFailed(migrationState))
        {
            _log.LogError($"Migration Failed. Migration ID: {migrationId}");
            _warningsCountLogger.LogWarningsCount(warningsCount);
            _log.LogInformation(migrationLogAvailableMessage);
            throw new OctoshiftCliException(failureReason);
        }

        _log.LogSuccess($"Migration completed (ID: {migrationId})! State: {migrationState}");
        _warningsCountLogger.LogWarningsCount(warningsCount);
        _log.LogInformation(migrationLogAvailableMessage);
    }

    private async Task<string> UploadArchiveToGitHub(string org, string archivePath)
    {
#pragma warning disable CA2007
        await using var archiveData = _fileSystemProvider.OpenRead(archivePath);
#pragma warning restore CA2007
        var githubOrgDatabaseId = await _githubApi.GetOrganizationDatabaseId(org).ConfigureAwait(false);

        _log.LogInformation("Uploading archive to GitHub Storage");
        var archiveName = $"{Guid.NewGuid()}.tar.gz";
        var authenticatedGitArchiveUri = await _githubApi.UploadArchiveToGithubStorage(githubOrgDatabaseId, archiveName, archiveData).ConfigureAwait(false);

        return authenticatedGitArchiveUri;
    }
}
