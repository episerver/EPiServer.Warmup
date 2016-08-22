using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using System.Web.SessionState;

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
            if (app.Request.UserAgent == "AlwaysOn" || app.Request.UserAgent == "SiteWarmup")
            {
                if (Interlocked.CompareExchange(ref _started, DateTime.Now, null)== null)
                {
                    Func<string, string> performRequest = (u) =>
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append(u);
                        sb.Append('\t');
                        try
                        {
                            Stopwatch timeTaken = new Stopwatch();
                            timeTaken.Start();
                            
                            ExecuteWarmupRequest(app.Context, new Uri(app.Request.Url, u));
                            timeTaken.Stop();
                            sb.Append(timeTaken.Elapsed.ToString());
                        }
                        catch (Exception ex)
                        {
                            sb.Append("FAILED");
                            sb.Append('\t');
                            sb.Append(ex.ToString());
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

        /// <summary>
        /// Executes a request through the pipeline
        /// </summary>
        /// <param name="thisContext"></param>
        /// <param name="uri"></param>
        private void ExecuteWarmupRequest(HttpContext thisContext, Uri uri)
        {
            var request = new HttpRequest(null, uri.Scheme + "://" + uri.Authority + uri.AbsolutePath, string.IsNullOrWhiteSpace(uri.Query) ? null : uri.Query.Substring(1));
            using (var sw = new StringWriter())
            {
                var response = new HttpResponse(sw);
                var context = new HttpContext(request, response);

                // Set up an anonymous identity to prevent NullReferenceExceptions carelessly accessing HttpContext.Current.User.Identity
                context.User = new GenericPrincipal(new GenericIdentity(""), new string[] { });

                // Required to be able to call Global.asax's GetVaryByCustomString from the OutputCacheAttribute
                context.ApplicationInstance = thisContext.ApplicationInstance;

                // If the application requires session state, provide it.
                var sessionContainer = new HttpSessionStateContainer("id",
                    new SessionStateItemCollection(),
                    new HttpStaticObjectsCollection(),
                    10, true,
                    HttpCookieMode.AutoDetect,
                    SessionStateMode.InProc, false);
                SessionStateUtility.AddHttpSessionStateToContext(context, sessionContainer);

                var contextBase = new HttpContextWrapper(context);
                var routeData = System.Web.Routing.RouteTable.Routes.GetRouteData(contextBase);

                // We shouldn't have to do this, but the way we are mocking the request doesn't seem to pass the querystring data through to the route data.
                foreach (string key in request.QueryString.Keys)
                {
                    if (!routeData.Values.ContainsKey(key))
                    {
                        routeData.Values.Add(key, request.QueryString[key]);
                    }
                }

                request.RequestContext.RouteData = routeData;
                request.RequestContext.HttpContext = contextBase;

                var httpHandler = routeData.RouteHandler.GetHttpHandler(request.RequestContext);
                var oldContext = HttpContext.Current;
                try
                {
                    HttpContext.Current = context;
                    httpHandler.ProcessRequest(context);
                }
                finally
                {
                    HttpContext.Current = oldContext;
                }
            }
        }
    }
}
