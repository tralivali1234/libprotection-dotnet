﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Web.UI;
using LibProtection.Injections;
using System.Configuration;
using System.Xml;
using System.Data;
using System.Data.SQLite;
using System.Web.Hosting;

namespace LibProtection.TestSite
{
    public partial class Default : Page
    {
        protected struct FormatResult
        {
            public bool Successfully { get; }
            public string FormattedValue { get; }

            public FormatResult(bool successfully, string formattedValue)
            {
                Successfully = successfully;
                FormattedValue = formattedValue;
            }
        }

        protected struct Example
        {
            public string Operation { get; set; }
            public Func<string, string[], string> FormatFunc { get; set; }
            public string Format { get; set; }
            public string Parameters { get; set; }
            public Func<string, string> TagBuilder { get; set; }
        }

        protected Dictionary<string, Example> Examples = new Dictionary<string, Example>
        {
            {
                "Html",
                new Example
                {
                    Operation = "Renders the given HTML markup on the client side.",
                    FormatFunc = FormatHelper<Html>,
                    Format = "<a href='{0}' onclick='alert(\"{1}\");return false'>{2}</a>",
                    Parameters = "Default.aspx\r\nHello from embedded JavaScript code!\r\nThis site's home page",
                    TagBuilder = s => s
                }
            },
            {
                "JavaScript",
                new Example
                {
                    Operation = "Executes the given JavaScript code on the client side.",
                    FormatFunc = FormatHelper<JavaScript>,
                    Format = "operationResult.innerText = '{0}';",
                    Parameters = "Hello from internal JavaScript code!",
                    TagBuilder = s => $"<script>\r\n{s}\r\n</script>"
                }
            },
            {
                "Sql",
                new Example
                {
                    Operation = "Executes the given SQL query on the sever side and outputs its results.",
                    FormatFunc = FormatHelper<Sql>,
                    Format = "SELECT * FROM myTable WHERE id = {0} AND myColumn = '{1}'",
                    Parameters = "1\r\nvalue1",
                    TagBuilder = SqlRequestTagBuilder
                }
            },
            {
                "Url",
                new Example
                {
                    Operation = "Uses the given URL on the client side to load and execute an external JavaScript code.",
                    FormatFunc = FormatHelper<Url>,
                    Format = "{0}/{1}",
                    Parameters = "Assets\r\njsFile.js",
                    TagBuilder = s => $"<script src=\"{s}\"></script>"
                }
            },
            {
                "FilePath",
                new Example
                {
                    Operation = "Reads content of the given local file on the server side and outputs it.",
                    FormatFunc = FormatHelper<FilePath>,
                    Format = HostingEnvironment.MapPath(@"~\Assets\{0}"),
                    Parameters = "textFile.txt",
                    TagBuilder = PathTagBuilder
                }
            },
            //new Example {FormatFunc = FormatHelper<Xpath>, Prefix="xpath", Formatter=@"descendant::bk:book[bk:author='{0}']", Replacer="' or ''='", TagBuilder = XPathTagBuilder },
            //new Example {FormatFunc = FormatHelper<Xml>, Prefix="xml", Formatter="<?xml version=\"1.0\"?>{0}", Replacer="<!DOCTYPE foo [<!ELEMENT foo ANY><!ENTITY xxe SYSTEM \"file:///textfile.txt\">]><foo>&xxe;</foo>", TagBuilder = XMLTagBuilder },
        };

        #region Builders
        protected static string XPathTagBuilder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) { return string.Empty; }

            var doc = new XmlDocument();
            doc.Load(System.IO.Path.Combine(HttpRuntime.AppDomainAppPath, "xmldocument.xml"));
            var root = doc.DocumentElement;

            // Add the namespace.
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("bk", "urn:newbooks-schema");

            var nodes = root?.SelectNodes(path, nsmgr);

            if (nodes == null) { return string.Empty; }

            var builder = new StringBuilder();
            foreach (XmlNode row in nodes)
            {
                if (row?["title"] != null && row["author"] != null)
                {
                    builder.AppendFormat("<br>{0} - {1}", row["title"].InnerText, row["author"].InnerText);
                }
            }
            return builder.ToString();
        }

        protected static string XmlTagBuilder(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) { return string.Empty; }

            var settings = new XmlReaderSettings {DtdProcessing = DtdProcessing.Parse};
            var xmlDoc = new XmlDocument {XmlResolver = new XmlUrlResolver()};
            var xmlBytes = Encoding.ASCII.GetBytes(xml);

            xmlDoc.Load(XmlReader.Create(new System.IO.MemoryStream(xmlBytes), settings));
            return xmlDoc.LastChild.OuterXml;
        }

        protected static string PathTagBuilder(string path)
        {
            var filePath = System.IO.Path.Combine(HttpRuntime.AppDomainAppPath, path);
            return System.IO.File.ReadAllText(filePath);
        }

        protected static string SqlRequestTagBuilder(string request)
        {
            if (string.IsNullOrWhiteSpace(request)) { return string.Empty; }
            string result;

            using (var connection = new SQLiteConnection(@"Data Source=|DataDirectory|\Database.sqlite;Version=3;Read Only=True;"))
            {
                using (var adapter = new SQLiteDataAdapter())
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = request;
                        adapter.SelectCommand = command;

                        using (var dataSet = new DataSet())
                        {
                            adapter.Fill(dataSet);

                            var builder = new StringBuilder();
                            foreach (DataRow row in dataSet.Tables[0].Rows)
                            {
                                builder.AppendFormat("Id: {0}, myColumn: '{1}'<br>", row["Id"], row["myColumn"]);
                            }
                            result = builder.ToString();
                        }
                    }
                    connection.Close();
                }
            }
            return result;
        }

        protected static string FormatHelper<T>(string format, object[] parameters) where T : LanguageProvider
        {
            return SafeString<T>.Format(format, parameters);
        }
        #endregion

        protected bool InputsAreDisabled
        {
            get
            {
                bool.TryParse(ConfigurationManager.AppSettings["DisableArbitraryFormatFeature"], out var disabledInputs);
                return disabledInputs;
            }
        }

        protected (string FormatResult, string OperationResult) GetResultsFor(Example example, string format,
            string parameters)
        {
            var formatResult = example.FormatFunc(
                format,
                parameters.Split(new[] {"\r\n", "\r", "\n"}, StringSplitOptions.None)
            );

            return (formatResult, example.TagBuilder(formatResult));
        }
    }
}