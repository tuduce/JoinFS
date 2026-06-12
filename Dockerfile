# Stage 1: publish for Alpine (linux-musl-x64)
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY JoinFS/JoinFS.csproj JoinFS/
RUN dotnet restore JoinFS/JoinFS.csproj -c CONSOLE -r linux-musl-x64
COPY . .
RUN dotnet publish JoinFS/JoinFS.csproj \
        -c CONSOLE \
        --runtime linux-musl-x64 \
        --self-contained false \
        --no-restore \
        -o /app/publish \
    && rm -rf /app/publish/de  /app/publish/es /app/publish/fr \
               /app/publish/it /app/publish/ko /app/publish/nl \
               /app/publish/pt /app/publish/ru \
               /app/publish/AIModel \
               /app/publish/*.pdb \
               /app/publish/Old-Readme.txt

# Stage 2: Alpine runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine
WORKDIR /JoinFS-CONSOLE
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "JoinFS-CONSOLE.dll"]
