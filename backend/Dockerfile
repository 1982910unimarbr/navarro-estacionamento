# Backend placeholder Dockerfile for ASP.NET Core app
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY . ./
# Expect project to be in ./src/Backend (to be created)
WORKDIR /src/Backend
RUN dotnet restore || true
RUN dotnet publish -c Release -o /app/publish || true

FROM build AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet","Backend.dll"]
