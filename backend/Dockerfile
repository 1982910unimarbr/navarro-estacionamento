FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ./Backend/ ./Backend/
WORKDIR /src/Backend
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 80
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet","Backend.dll"]
