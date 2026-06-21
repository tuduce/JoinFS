# Stage 1: publish for Alpine (linux-musl-x64)
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY JoinFS/JoinFS.csproj JoinFS/
RUN dotnet restore JoinFS/JoinFS.csproj -r linux-musl-x64 /p:configuration=CONSOLE 
COPY . .
RUN dotnet publish JoinFS/JoinFS.csproj \
		-r linux-musl-x64 \
        /p:configuration=CONSOLE \
        --self-contained false \
        --no-restore \
        -o /app/publish \
    && rm -rf \
			   /app/publish/de  /app/publish/es /app/publish/fr \
               /app/publish/it /app/publish/ko /app/publish/nl \
               /app/publish/pt /app/publish/ru \
               /app/publish/AIModel \
               /app/publish/*.pdb \
               /app/publish/Old-Readme.txt

# Stage 2: Alpine runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine
EXPOSE 6112
WORKDIR /JoinFS-CONSOLE
COPY --from=build /app/publish .
RUN mv JoinFS-CONSOLE JoinFS
ENTRYPOINT ["dotnet", "JoinFS-CONSOLE.dll"]
