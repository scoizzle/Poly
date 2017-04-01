FROM microsoft/dotnet
WORKDIR /opt/polyhttp/

ADD Publish .
ENTRYPOINT ["dotnet", "HttpServer.dll"]