# EPiServer.Warmup
An HttpModule (registered via web.config) that captures the Azure warm-up request and hits a number of (configurable) local URLs.

This is a module to warmup a site instance. The module captures any incoming requests 
having the UserAgent set to "SiteWarmup", which Azure uses for the built-in warmup 
request to the site root. Because it captures that request, you must yourself 
register "/" in the warmup.txt-file that lists the URLs to hit when this module
is active.

Installation instructions
-------------------------
* Add the EPiServer.Warmup.dll to the bin-folder of your application
* Reference it in your web.config as demonstrated below
* Set up your warmup.txt-file

```xml
<system.webServer>
	<modules runAllManagedModulesForAllRequests="true">
		...
		<add name="WarmupModule" type="EPiServer.Warmup.WarmupModule, EPiServer.Warmup"/>
	</modules>
</system.webServer>
```

The warmup.txt file
-------------------
If present, the module will open it up and fire off a request to each URL, absolute or
relative, in the text file. Note that the URL, even when absolute, will unconditionally
result in a request to localhost, with the hostname taken from the URL. If relative, it
issues a request using the Host-header taken from the warmup request.

This file, placed in the root of the application list one URL per line. It allows empty
and lines starting with the '#' character which it ignores. This would be a valid
example:

```
## This is a sample warmup.txt file for the warmup module
# Request the site root
/
# .com requests
http://example.com/about-us/
http://example.com/catalog/
http://example.com/search/?query=bicycle
http://example.com/ProductsSearch?query=bicycle&skip=0&take=25
# .se requests
http://example.se/om-oss/
http://example.se/katalog/
http://example.se/sok/?query=cykel
http://example.se/ProductsSearch?query=cykel&skip=0&take=25
```

