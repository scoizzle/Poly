using System;
using System.Diagnostics;

using Xunit;
using Xunit.Abstractions;

namespace Poly.UnitTests {

    public class JSON {
        [Fact]
        public void Parse() {
            Assert.True(Data.JSON.TryParse(TestData, out Data.JSON js));

            Assert.Equal("cofaxCDS", js["web-app.servlet.0.servlet-name"]);
            Assert.Equal("cofax.tld", js["web-app.taglib.taglib-uri"]);
        }

        [Fact]
        [Conditional("RELEASE")]
        public void Parse_Performance() {
            Log.Benchmark("Data.JSON.TryParse", 1000000, () => Data.JSON.TryParse(TestData, out Data.JSON _));
        }

        public const string TestData = "{\"web-app\":{\"servlet\":[{\"servlet-name\":\"cofaxCDS\",\"servlet-class\":\"org.cofax.cds.CDSServlet\",\"init-param\":{\"configGlossary:installationAt\":\"Philadelphia, PA\",\"configGlossary:adminEmail\":\"ksm@pobox.com\",\"configGlossary:staticPath\":\"/content/static\",\"configGlossary:poweredBy\":\"Cofax\",\"searchEngineListTemplate\":\"forSearchEnginesList.htm\",\"searchEngineFileTemplate\":\"forSearchEngines.htm\",\"configGlossary:poweredByIcon\":\"/images/cofax.gif\",\"templateProcessorClass\":\"org.cofax.WysiwygTemplate\",\"templateLoaderClass\":\"org.cofax.FilesTemplateLoader\",\"defaultListTemplate\":\"listTemplate.htm\",\"defaultFileTemplate\":\"articleTemplate.htm\",\"cacheTemplatesTrack\":100,\"cacheTemplatesStore\":50,\"cachePagesDirtyRead\":10,\"templatePath\":\"templates\",\"useDataStore\":true,\"dataStoreUrl\":\"jdbc:microsoft:sqlserver://LOCALHOST:1433;DatabaseName=goon\",\"templateOverridePath\":\"\",\"searchEngineRobotsDb\":\"WEB-INF/robots.db\",\"useJSP\":false,\"jspListTemplate\":\"listTemplate.jsp\",\"jspFileTemplate\":\"articleTemplate.jsp\",\"cachePagesTrack\":200,\"cachePagesStore\":100,\"dataStoreDriver\":\"com.microsoft.jdbc.sqlserver.SQLServerDriver\",\"cachePackageTagsTrack\":200,\"cachePackageTagsStore\":200,\"cacheTemplatesRefresh\":15,\"cachePackageTagsRefresh\":60,\"cachePagesRefresh\":10,\"dataStorePassword\":\"dataStoreTestQuery\",\"dataStoreClass\":\"org.cofax.SqlDataStore\",\"redirectionClass\":\"org.cofax.SqlRedirection\",\"dataStoreName\":\"cofax\",\"dataStoreUser\":\"sa\",\"dataStoreTestQuery\":\"SET NOCOUNT ON;select test=\"}},{\"servlet-name\":\"cofaxEmail\",\"servlet-class\":\"org.cofax.cds.EmailServlet\",\"init-param\":{\"mailHost\":\"mail1\",\"mailHostOverride\":\"mail2\"}},{\"servlet-name\":\"cofaxAdmin\",\"servlet-class\":\"org.cofax.cds.AdminServlet\"},{\"servlet-name\":\"fileServlet\",\"servlet-class\":\"org.cofax.cds.FileServlet\"},{\"servlet-name\":\"cofaxTools\",\"servlet-class\":\"org.cofax.cms.CofaxToolsServlet\",\"init-param\":{\"templatePath\":\"toolstemplates/\",\"adminGroupID\":4,\"log\":1,\"logLocation\":\"/usr/local/tomcat/logs/CofaxTools.log\",\"logMaxSize\":\"\",\"betaServer\":true,\"dataLog\":1,\"dataLogLocation\":\"/usr/local/tomcat/logs/dataLog.log\",\"removePageCache\":\"/content/admin/remove?cache=pages&id=\",\"dataLogMaxSize\":\"\",\"removeTemplateCache\":\"/content/admin/remove?cache=templates&id=\",\"fileTransferFolder\":\"/usr/local/tomcat/webapps/content/fileTransferFolder\",\"lookInContext\":1}}],\"servlet-mapping\":{\"cofaxCDS\":\"/\",\"cofaxEmail\":\"/cofaxutil/aemail/*\",\"cofaxAdmin\":\"/admin/*\",\"cofaxTools\":\"/tools/*\",\"fileServlet\":\"/static/*\"},\"taglib\":{\"taglib-uri\":\"cofax.tld\",\"taglib-location\":\"/WEB-INF/tlds/cofax.tld\"}}}";
    }
}