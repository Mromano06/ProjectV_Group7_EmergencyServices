#Build stage
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

#copy the project file and restore dependcies
COPY ["MyApplication.Api.csproj","."]
RUN dotnet restore "MyApplication.Api.csproj"

#copy the remaining source files
COPY . .

#Build and publish the application
RUN dotnet publish "MyApplication.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore


#runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app

#MAKE the API listen on all container network interfaces
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT [ "dotnet","MyApplication.Api.dll" ]
