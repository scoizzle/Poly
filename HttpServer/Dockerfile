FROM microsoft/dotnet
WORKDIR /opt/polyhttp/

ADD Publish .
ENTRYPOINT ["dotnet", "Poly.Http.Server.dll"]