# 1. Uporabi uradni .NET 8.0 runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# 2. Build faza
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["DoggyDrop.csproj", "./"]
RUN dotnet restore "DoggyDrop.csproj"

COPY . .
RUN dotnet publish "DoggyDrop.csproj" -c Release -o /app/publish

# 3. Končna faza
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DoggyDrop.dll"]
