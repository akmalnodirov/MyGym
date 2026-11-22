FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Directory.Build.props carries TargetFramework/Nullable/warnings-as-errors for every project;
# Directory.Packages.props is the central package-version source for every PackageReference — MSBuild
# finds both by walking up from the .csproj being restored, so they must land at this same relative
# depth (repo root) or restore silently falls back to defaults / fails with NETSDK1004 for the
# missing central version catalogue. global.json in this repo carries no "sdk" version pin today
# (only a test-runner setting), so its absence would not currently break SDK selection for this
# publish-only stage — but it is copied anyway so the build stays correct if a pin is ever added.
# MyGym.sln itself is deliberately not copied (minor fix-before-merge item 6): restore and publish
# below target the .csproj directly, and neither ever references the solution file.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet restore src/Host/MyGym.Api/MyGym.Api.csproj
RUN dotnet publish src/Host/MyGym.Api/MyGym.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyGym.Api.dll"]
