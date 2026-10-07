# Runtime image for the API. The build context is the output of
#   dotnet publish src/IPS.Middleware.Api/IPS.Middleware.Api.csproj --configuration Release --output <directory>
# so the image needs no NuGet access of its own (the build host restores, which also works behind a TLS-inspecting proxy).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY . .
# Everything else, including the database and every endpoint, is configured through environment variables.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "IPS.Middleware.Api.dll"]
