/////////////////////////////////////////////////////////////////////
// ADDINS
/////////////////////////////////////////////////////////////////////

#addin "nuget:?package=Polly&version=5.0.6"
#addin "nuget:?package=NuGet.Core&version=2.12.0"

//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////

#tool "nuget:https://dotnet.myget.org/F/nuget-build/?package=NuGet.CommandLine&version=4.3.0-beta1-2361&prerelease"

///////////////////////////////////////////////////////////////////////////////
// USINGS
///////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Polly;
using NuGet;

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var platform = Argument("platform", "AnyCPU");
var configuration = Argument("configuration", "Release");

///////////////////////////////////////////////////////////////////////////////
// CONFIGURATION
///////////////////////////////////////////////////////////////////////////////

var MainRepo = "VitalElement/debugger-libs";
var MasterBranch = "avalon-studio";
var ReleasePlatform = "Any CPU";
var ReleaseConfiguration = "Release";

///////////////////////////////////////////////////////////////////////////////
// PARAMETERS
///////////////////////////////////////////////////////////////////////////////

var isPlatformAnyCPU = StringComparer.OrdinalIgnoreCase.Equals(platform, "Any CPU");
var isPlatformX86 = StringComparer.OrdinalIgnoreCase.Equals(platform, "x86");
var isPlatformX64 = StringComparer.OrdinalIgnoreCase.Equals(platform, "x64");
var isLocalBuild = BuildSystem.IsLocalBuild;
var isRunningOnUnix = IsRunningOnUnix();
var isRunningOnWindows = IsRunningOnWindows();
var isRunningOnAppVeyor = BuildSystem.AppVeyor.IsRunningOnAppVeyor;
var isPullRequest = BuildSystem.AppVeyor.Environment.PullRequest.IsPullRequest;
var isMainRepo = StringComparer.OrdinalIgnoreCase.Equals(MainRepo, BuildSystem.AppVeyor.Environment.Repository.Name);
var isMasterBranch = StringComparer.OrdinalIgnoreCase.Equals(MasterBranch, BuildSystem.AppVeyor.Environment.Repository.Branch);
var isTagged = BuildSystem.AppVeyor.Environment.Repository.Tag.IsTag 
               && !string.IsNullOrWhiteSpace(BuildSystem.AppVeyor.Environment.Repository.Tag.Name);
var isReleasable = StringComparer.OrdinalIgnoreCase.Equals(ReleasePlatform, platform) 
                   && StringComparer.OrdinalIgnoreCase.Equals(ReleaseConfiguration, configuration);
var isMyGetRelease = true;

///////////////////////////////////////////////////////////////////////////////
// VERSION
///////////////////////////////////////////////////////////////////////////////

var version = "0.2.0";

if (isRunningOnAppVeyor)
{
    if (isTagged)
    {
        // Use Tag Name as version
        version = BuildSystem.AppVeyor.Environment.Repository.Tag.Name;
    }
    else
    {
        // Use AssemblyVersion with Build as version
        version += "-build" + EnvironmentVariable("APPVEYOR_BUILD_NUMBER") + "-alpha";
    }
}

var editbin = @"C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\VC\Tools\MSVC\14.10.25017\bin\HostX86\x86\editbin.exe";

///////////////////////////////////////////////////////////////////////////////
// DIRECTORIES
///////////////////////////////////////////////////////////////////////////////

var artifactsDir = (DirectoryPath)Directory("./artifacts");
var zipRootDir = artifactsDir.Combine("zip");
var nugetRoot = artifactsDir.Combine("nuget");

var fileZipSuffix = ".zip";

var buildDirs = GetDirectories("./**/bin/**") + 
    GetDirectories("./**/obj/**") +     
    GetDirectories("./artifacts/**/zip/**");

var netCoreAppsRoot= ".";
var netCoreApps = new string[] { "Mono.Debugging", "Mono.Debugging.Win32" };
var netCoreProjects = netCoreApps.Select(name => 
{    
    Information(string.Format("{0}/{1}/{1}.NetCore2.csproj", netCoreAppsRoot, name));

    return new 
    {
        Path = string.Format("{0}/{1}", netCoreAppsRoot, name),
        Name = name,
        
        Framework = XmlPeek(string.Format("{0}/{1}/{1}.csproj", netCoreAppsRoot, name), "//*[local-name()='TargetFramework']/text()"),
        //Runtimes = XmlPeek(string.Format("{0}/{1}/{1}.csproj", netCoreAppsRoot, name), "//*[local-name()='RuntimeIdentifiers']/text()").Split(';')    
    };
}).ToList();

///////////////////////////////////////////////////////////////////////////////
// NUGET NUSPECS
///////////////////////////////////////////////////////////////////////////////

// Key: Package Id
// Value is Tuple where Item1: Package Version, Item2: The *.csproj/*.props file path.
var packageVersions = new Dictionary<string, IList<Tuple<string,string>>>();

System.IO.Directory.EnumerateFiles(((DirectoryPath)Directory(".")).FullPath, "*.csproj", SearchOption.AllDirectories)
    .ToList()
    .ForEach(fileName => {
    var xdoc = XDocument.Load(fileName);
    foreach (var reference in xdoc.Descendants().Where(x => x.Name.LocalName == "PackageReference"))
    {
        var name = reference.Attribute("Include").Value;
        var versionAttribute = reference.Attribute("Version");
        var packageVersion = versionAttribute != null 
            ? versionAttribute.Value 
            : reference.Elements().First(x=>x.Name.LocalName == "Version").Value;
        IList<Tuple<string, string>> versions;
        packageVersions.TryGetValue(name, out versions);
        if (versions == null)
        {
            versions = new List<Tuple<string, string>>();
            packageVersions[name] = versions;
        }
        versions.Add(Tuple.Create(packageVersion, fileName));
    }
});

Information("Checking installed NuGet package dependencies versions:");

packageVersions.ToList().ForEach(package =>
{
    var packageVersion = package.Value.First().Item1;
    bool isValidVersion = package.Value.All(x => x.Item1 == packageVersion);
    if (!isValidVersion)
    {
        Information("Error: package {0} has multiple versions installed:", package.Key);
        foreach (var v in package.Value)
        {
            Information("{0}, file: {1}", v.Item1, v.Item2);
        }
        throw new Exception("Detected multiple NuGet package version installed for different projects.");
    }
});

Information("Setting NuGet package dependencies versions:");


var nuspecNuGetBehaviors = new NuGetPackSettings()
{
    Id = "VitalElement.Debugging",
    Version = version,
    Authors = new [] { "Dan Walmsley" },
    Owners = new [] { "VitalElement" },
    LicenseUrl = new Uri("http://opensource.org/licenses/MIT"),
    ProjectUrl = new Uri("https://github.com/danwalmsley/debugger-libs/"),
    RequireLicenseAcceptance = false,
    Symbols = false,
    NoPackageAnalysis = true,
    Description = "A port Mono.Debugging to AvalonStudio.",
    Copyright = "Copyright 2017",
    Tags = new [] { "Debugging", "AvalonStudio" },
    Dependencies = new []
    {        
        new NuSpecDependency { Id = "ICSharpCode.NRefactory", Version = "5.5.1" }
    },
    Files = new []
    {
        new NuSpecContent { Source = "Mono.Debugging/bin/" + configuration + "/net46/Mono.Debugging.dll", Target = "lib/net46" },
    },
    BasePath = Directory("./"),
    OutputDirectory = nugetRoot
};

var nuspecNuGetSettings = new List<NuGetPackSettings>();

nuspecNuGetSettings.Add(nuspecNuGetBehaviors);

nuspecNuGetBehaviors = new NuGetPackSettings()
{
    Id = "VitalElement.Debugging.Win32",
    Version = version,
    Authors = new [] { "Dan Walmsley" },
    Owners = new [] { "VitalElement" },
    LicenseUrl = new Uri("http://opensource.org/licenses/MIT"),
    ProjectUrl = new Uri("https://github.com/danwalmsley/debugger-libs/"),
    RequireLicenseAcceptance = false,
    Symbols = false,
    NoPackageAnalysis = true,
    Description = "A port Mono.Debugging.Win32 to AvalonStudio.",
    Copyright = "Copyright 2017",
    Tags = new [] { "Debugging", "AvalonStudio" },
    Dependencies = new []
    {        
        new NuSpecDependency { Id = "ICSharpCode.NRefactory", Version = "5.5.1" }
    },
    Files = new []
    {
        new NuSpecContent { Source = "Mono.Debugging.Win32/bin/" + configuration + "/net46/Mono.Debugging.Win32.dll", Target = "lib/net46" },
        new NuSpecContent { Source = "Mono.Debugging.Win32/bin/" + configuration + "/net46/CorApi.NetCore2.dll", Target = "lib/net46" },
        new NuSpecContent { Source = "Mono.Debugging.Win32/bin/" + configuration + "/net46/CorApi2.NetCore2.dll", Target = "lib/net46" },
    },
    BasePath = Directory("./"),
    OutputDirectory = nugetRoot
};

nuspecNuGetSettings.Add(nuspecNuGetBehaviors);

var nugetPackages = nuspecNuGetSettings.Select(nuspec => {
    return nuspec.OutputDirectory.CombineWithFilePath(string.Concat(nuspec.Id, ".", nuspec.Version, ".nupkg"));
}).ToArray();

///////////////////////////////////////////////////////////////////////////////
// INFORMATION
///////////////////////////////////////////////////////////////////////////////

Information("Building version {0} of DebuggerLibs ({1}, {2}, {3}) using version {4} of Cake.", 
    version,
    platform,
    configuration,
    target,
    typeof(ICakeContext).Assembly.GetName().Version.ToString());

if (isRunningOnAppVeyor)
{
    Information("Repository Name: " + BuildSystem.AppVeyor.Environment.Repository.Name);
    Information("Repository Branch: " + BuildSystem.AppVeyor.Environment.Repository.Branch);
}

Information("Target: " + target);
Information("Platform: " + platform);
Information("Configuration: " + configuration);
Information("IsLocalBuild: " + isLocalBuild);
Information("IsRunningOnUnix: " + isRunningOnUnix);
Information("IsRunningOnWindows: " + isRunningOnWindows);
Information("IsRunningOnAppVeyor: " + isRunningOnAppVeyor);
Information("IsPullRequest: " + isPullRequest);
Information("IsMainRepo: " + isMainRepo);
Information("IsMasterBranch: " + isMasterBranch);
Information("IsTagged: " + isTagged);
Information("IsReleasable: " + isReleasable);
Information("IsMyGetRelease: " + isMyGetRelease);
Information("IsNuGetRelease: " + isNuGetRelease);


///////////////////////////////////////////////////////////////////////////////
// TASKS
/////////////////////////////////////////////////////////////////////////////// 

Task("Clean")
.Does(()=>{
    CleanDirectories(buildDirs);
    CleanDirectory(nugetRoot);
    CleanDirectory(zipRootDir);
});

Task("Restore-NetCore")
    .IsDependentOn("Clean")
    .Does(() =>
{    
    foreach (var project in netCoreProjects)
    {
        DotNetCoreRestore(project.Path);
    }
});

Task("Build-NetCore")
    .IsDependentOn("Restore-NetCore")
    .Does(() =>
{
    foreach (var project in netCoreProjects)
    {
        Information("Building: {0}", project.Name);

        var settings = new DotNetCoreBuildSettings {
            Configuration = configuration
        };

        if (!IsRunningOnWindows())
        {
            settings.Framework = "netcoreapp1.1";
        }

        DotNetCoreBuild(project.Path, settings);
    }
});

Task("Create-NuGet-Packages")
    .IsDependentOn("Build-NetCore")
    .Does(() =>
{
    foreach(var nuspec in nuspecNuGetSettings)
    {
        NuGetPack(nuspec);
    }
});

Task("Publish-MyGet")
    .IsDependentOn("Create-NuGet-Packages")
    .WithCriteria(() => !isLocalBuild)
    .WithCriteria(() => !isPullRequest)
    .WithCriteria(() => isMainRepo)
    .WithCriteria(() => isMasterBranch)        
    .WithCriteria(()=> isRunningOnAppVeyor)
    .WithCriteria(()=> isTagged)
    .Does(() =>
{
    var apiKey = EnvironmentVariable("MYGET_API_KEY");
    if(string.IsNullOrEmpty(apiKey)) 
    {
        throw new InvalidOperationException("Could not resolve MyGet API key.");
    }

    var apiUrl = EnvironmentVariable("MYGET_API_URL");
    if(string.IsNullOrEmpty(apiUrl)) 
    {
        throw new InvalidOperationException("Could not resolve MyGet API url.");
    }

    foreach(var nupkg in nugetPackages)
    {
        NuGetPush(nupkg, new NuGetPushSettings {
            Source = apiUrl,
            ApiKey = apiKey
        });
    }
})
.OnError(exception =>
{
    Information("Publish-MyGet Task failed, but continuing with next Task...");
});

Task("Default")
    .IsDependentOn("Restore-NetCore")
    .IsDependentOn("Build-NetCore")    
    .IsDependentOn("Publish-MyGet");

RunTarget(target);
