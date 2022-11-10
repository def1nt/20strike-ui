FROM mcr.microsoft.com/dotnet/runtime
FROM mcr.microsoft.com/dotnet/aspnet

WORKDIR /20strike-ui

COPY bin/Debug/net7.0/publish ./
COPY Pages ./
COPY Shared ./

EXPOSE 7156/tcp
EXPOSE 5247/tcp

CMD dotnet 20strike-ui.dll