using System.Collections.Generic;
using JetBrains.Annotations;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.MinVer;
using Nuke.Common.Utilities.Collections;
using Nuke.Components;
using Serilog;

namespace ExtensionsBuild;

[DisableDefaultOutput(
    DefaultOutput.Logo,
    DefaultOutput.TargetOutcome,
    DefaultOutput.BuildOutcome)]
class Build : NukeBuild, IPublish
{
    public static int Main() => Execute<Build>(x => x.Print);

    MinVer MinVerInfo { get; set; }
    [GitRepository] GitRepository GitRepo { get; }

    [Parameter] readonly string MinVerTagPrefix;
    [Parameter] readonly string MinVerPreReleaseIdentifiers;
    [Parameter] readonly string MinVerIgnoreHeight;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath SamplesDirectory => RootDirectory / "samples";


    Target Print => target => target
        .Unlisted()
        .Description("Prints resolved project metadata")
        .Executes(() =>
        {
            var solution = (this as IHazSolution).Solution;
            Log.Information("Solution path = {Path}", solution.Path);
            Log.Information("Solution directory = {Dir}", solution.Directory);
            Log.Information("Source Repository = {GitUrl} / [{GitBranch}]", GitRepo.HttpsUrl, GitRepo.Branch);
            Log.Information("Project Version {Version}", MinVerInfo.Version);
            Log.Information("Git Repository {GitRepoUrl}", GitRepo.HttpsUrl);
        });

    Target CleanSamples => target => target
        .Description("Clean sample projects directories")
        .Executes(() =>
        {
            SamplesDirectory.GlobDirectories("**/{obj,bin}")
                .ForEach(static dir => dir.DeleteDirectory());
        });

    Target CleanTests => target => target
        .Description("Clean test test directories")
        .Executes(() =>
        {
            TestsDirectory.GlobDirectories("**/{obj,bin}")
                .ForEach(static dir => dir.DeleteDirectory());
        });

    Target CleanSource => target => target
        .Description("Clean source projects directories")
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/{obj,bin}")
                .ForEach(static dir => dir.DeleteDirectory());
        });

    [PublicAPI]
    Target Clean => target => target
        .Description("Clean all projects directories")
        .DependsOn(CleanSamples, CleanTests, CleanSource)
        .TryDependentFor<IRestore>()
        .Executes(() =>
        {
            (this as IHazArtifacts).ArtifactsDirectory
                .CreateOrCleanDirectory();
        });

    public IEnumerable<Project> TestProjects => (this as IHazSolution).Solution.GetAllProjects("*.Tests");

    public Configure<DotNetPackSettings> PackSettings => settings => settings
        .SetVersion(MinVerInfo.Version)
        .SetRepositoryUrl(GitRepo.HttpsUrl);

    protected override void OnBuildInitialized()
    {
        base.OnBuildInitialized();
        var version = MinVerTasks.MinVer(settings => settings
                .SetTagPrefix(MinVerTagPrefix)
                .SetVerbosity(MinVerVerbosity.Info)
                .SetProcessEnvironmentVariable("MinVerDefaultPreReleaseIdentifiers", MinVerPreReleaseIdentifiers)
                .SetProcessEnvironmentVariable("MinVerIgnoreHeight", MinVerIgnoreHeight )
                .DisableProcessLogOutput()
            )
            .Result;

        MinVerInfo = version;
    }
}
