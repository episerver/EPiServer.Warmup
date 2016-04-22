using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;

namespace EPiServer.Warmup
{
    public class WarmupModule : IHttpModule
    {
        private static object _started = null;
        private static string _warmupLog = null;
        private static long _executed;

        public void Dispose()
        {
        }

        public void Init(HttpApplication context)
        {
            context.BeginRequest += Context_BeginRequest;
        }

        private void Context_BeginRequest(object sender, EventArgs e)
        {
            var app = sender as HttpApplication;
            if (app.Request.UserAgent == "SiteWarmup")
            {
                if (Interlocked.CompareExchange(ref _started, DateTime.Now, null)== null)
                {
                    Func<string, string> performRequest = (u) =>
                    {
                        HttpStatusCode status;
                        StringBuilder sb = new StringBuilder();
                        sb.Append(u);
                        sb.Append('\t');
                        try
                        {
                            Stopwatch timeTaken = new Stopwatch();
                            timeTaken.Start();
                            status = ExecuteWarmupRequest(new Uri(app.Request.Url, u));
                            timeTaken.Stop();
                            sb.Append(status);
                            sb.Append('\t');
                            sb.Append(timeTaken.Elapsed.ToString());
                        }
                        catch (Exception ex)
                        {
                            sb.Append("FAILED");
                            sb.Append('\t');
                            sb.Append(ex.Message);
                        }
                        return sb.ToString();
                    };

                    int scriptTimeout;
                    if (!int.TryParse(ConfigurationManager.AppSettings["episerver.warmup.scripttimeout"], out scriptTimeout))
                    {
                        scriptTimeout = 1800;   // 1800 = 20min
                    }
                    app.Server.ScriptTimeout = scriptTimeout; 

                    var warmupLog = new StringBuilder();
                    var f = app.Server.MapPath("~/warmup.txt");
                    if (File.Exists(f))
                    {
                        using (var sr = File.OpenText(f))
                        {
                            string l;
                            while((l = sr.ReadLine()) != null)
                            {
                                if (l.StartsWith("#") || string.IsNullOrWhiteSpace(l))
                                    continue;
                                warmupLog.AppendLine(performRequest(l));
                                Interlocked.Increment(ref _executed);
                            }
                        }
                    } else
                    {
                        warmupLog.AppendLine(performRequest("/"));
                        Interlocked.Increment(ref _executed);
                    }
                    Interlocked.Exchange(ref _warmupLog, warmupLog.ToString());
                }
                
                app.Response.Clear();
                app.Response.ClearHeaders();
                app.Response.ContentType = "text/plain";

                app.Response.Write(_started);
                app.Response.Write(Environment.NewLine);

                if (_warmupLog != null)
                {
                    app.Response.Write(string.Format("Complete ({0}).", Interlocked.Read(ref _executed)));
                    app.Response.Write(Environment.NewLine);
                    app.Response.Write(_warmupLog.ToString());
                }
                else {
                    
                    app.Response.Write(string.Format("Still ongoing ({0}).", Interlocked.Read(ref _executed)));
                }

                app.Response.StatusCode = 200;
                app.Response.StatusDescription = "OK";

                app.Response.Flush();
                app.CompleteRequest();
            }
        }

        private HttpStatusCode ExecuteWarmupRequest(Uri uri)
        {
            UriBuilder ub = new UriBuilder(uri);
            ub.Host = "127.0.0.1";
            var req = WebRequest.Create(ub.Uri) as HttpWebRequest;
            if (req == null)
                return 0;

            req.Timeout = 300000;
            req.Host = uri.Host;
            HttpWebResponse resp;
            try
            {
                resp = req.GetResponse() as HttpWebResponse;
            }
            catch (WebException wex)
            {
                if (wex.Response == null || wex.Status != WebExceptionStatus.ProtocolError)
                    throw;

                resp = wex.Response as HttpWebResponse;
            }

            if (resp == null)
                return 0;

            using (resp)
            {
                return resp.StatusCode;
            }
        }
    }
}
