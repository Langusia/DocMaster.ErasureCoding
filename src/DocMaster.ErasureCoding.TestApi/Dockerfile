FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base

# Install ISA-L from official repository
RUN apt-get update && \
    apt-get install -y libisal2 && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/DocMaster.ErasureCoding.TestApi/DocMaster.ErasureCoding.TestApi.csproj", "TestApi/"]
COPY ["src/DocMaster.ErasureCoding/DocMaster.ErasureCoding.csproj", "Library/"]

RUN dotnet restore "TestApi/DocMaster.ErasureCoding.TestApi.csproj"

# Copy source
COPY src/DocMaster.ErasureCoding.TestApi/ TestApi/
COPY src/DocMaster.ErasureCoding/ Library/

RUN dotnet build "TestApi/DocMaster.ErasureCoding.TestApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TestApi/DocMaster.ErasureCoding.TestApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV PORT=80

ENTRYPOINT ["dotnet", "DocMaster.ErasureCoding.TestApi.dll"]
