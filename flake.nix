{
  inputs = {
    flakelight.url = "github:nix-community/flakelight";
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
  };

  outputs = {
    flakelight,
    self,
    ...
  }:
    flakelight ./. {
      packages = {
        uasset-api = {pkgs, ...}: let
          commitSha = self.rev or self.dirtyRev or "unknown-commit";
          shortSha = builtins.substring 0 7 commitSha;
        in
          pkgs.buildDotnetModule {
            name = "UAssetAPI-Kawaii";
            version = "master-${shortSha}";
            src = ./.;
            dotnet-sdk = pkgs.dotnetCorePackages.sdk_8_0;
            nugetDeps = ./nix/deps.json;
            leaveDotGit = true;
          };
        kawaii-physics-binding = {pkgs, ...}: let
          commitSha = self.rev or self.dirtyRev or "unknown-commit";
          shortSha = builtins.substring 0 7 commitSha;
        in
          pkgs.buildDotnetModule {
            pname = "KawaiiPhysicsBinding";
            version = "0.0.0-${shortSha}";
            src = ./.;
            projectFile = "KawaiiPhysicsBinding/KawaiiPhysicsBinding.csproj";
            dotnet-sdk = pkgs.dotnetCorePackages.sdk_8_0;
            nugetDeps = ./nix/deps.json;
            dotnetBuildFlags = [ "-p:KawaiiPhysicsBindingSelfContained=false" ];
            dotnetInstallFlags = [ "-p:KawaiiPhysicsBindingSelfContained=false" "-p:UseAppHost=false" ];
            executables = [ ];
          };
        default = {pkgs, ...}: pkgs.uasset-api;
      };
    };
}
