using System.Reflection;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using MiniCodex.Codex;
using MiniCodex.Services;
using MiniCodex.Views;

internal static partial class Program
{
    private static void WaitUntil(Func<bool> completed, string description)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (!completed() && DateTimeOffset.UtcNow < deadline) Pump(25);
        Expect(completed(), description);
    }
    private static T Await<T>(Task<T> task, string description)
    {
        WaitUntil(() => task.IsCompleted, description);
        return task.GetAwaiter().GetResult();
    }
    private static AccountBrowserWindow ShowBrowser(string folder)
    {
        var window = new AccountBrowserWindow("Test account", "test@example.invalid", UsageResetPage.Url, folder)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000
        };
        var browser = (WebView2)window.FindName("Browser");
        // Keep this smoke check offline; no user website or login is requested.
        browser.NavigationStarting += (_, e) => { if (e.Uri != "about:blank" && !e.Uri.StartsWith("https://mini-codex.invalid/", StringComparison.Ordinal)) e.Cancel = true; };
        window.Show();
        Expect(Await(window.Ready, "Embedded browser initialization completes"), "Embedded browser initializes: " + ((TextBlock)window.FindName("PageStatus")).Text);
        return window;
    }

    private static void CheckUsageResetBrowser(AccountRuntime runtime, Guid profileId, string folder)
    {
        Expect(!runtime.HasResetCount && runtime.ResetLabel.Length == 0, "Unavailable reset count has no reset action label");
        var count = typeof(AccountRuntime).GetField("_availableResetCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
        count.SetValue(runtime, 0L);
        Expect(runtime.HasResetCount && runtime.ResetLabel == "0 rst", "Zero resets still provide the page action");
        count.SetValue(runtime, 2L);
        Equal("2 rst", runtime.ResetLabel, "Reset button labels the reported count");
        Expect(!runtime.Usage.Contains("rst", StringComparison.Ordinal), "Usage text does not duplicate the reset button");
        count.SetValue(runtime, null);

        var firstFolder = UsageResetPage.BrowserFolder(profileId, folder, folder);
        var secondFolder = UsageResetPage.BrowserFolder(Guid.NewGuid(), folder, folder);
        Expect(firstFolder != secondFolder, "Accounts receive separate browser storage");
        Expect(firstFolder != UsageResetPage.BrowserFolder(profileId, System.IO.Path.Combine(folder, "another-home"), folder), "Changing Codex home isolates browser sign-in");
        Equal(firstFolder, UsageResetPage.BrowserFolder(profileId, folder.ToUpperInvariant() + System.IO.Path.DirectorySeparatorChar, folder), "Equivalent home paths retain the browser profile");
        foreach (var blocked in new[] { "file:///C:/example", "javascript:alert(1)", "data:text/html,example", "ms-settings:test", "http://example.invalid/", "https://user:secret@example.invalid/" })
            Expect(!UsageResetPage.CanNavigate(blocked), "Browser rejects non-HTTPS or credential-bearing address: " + blocked.Split(':')[0]);
        Expect(UsageResetPage.CanNavigate(UsageResetPage.Url.AbsoluteUri), "The official usage page can open");

        var first = ShowBrowser(firstFolder);
        var view = (WebView2)first.FindName("Browser");
        var finished = false;
        view.CoreWebView2.NavigationCompleted += (_, e) => { if (e.IsSuccess) finished = true; };
        System.IO.File.WriteAllText(System.IO.Path.Combine(firstFolder, "index.html"), "<!doctype html><html><body><h1>Browser works</h1></body></html>");
        view.CoreWebView2.SetVirtualHostNameToFolderMapping("mini-codex.invalid", firstFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        view.CoreWebView2.Navigate("https://mini-codex.invalid/index.html");
        WaitUntil(() => finished, "Embedded browser renders local content");
        Equal("\"Browser works\"", Await(view.ExecuteScriptAsync("document.querySelector('h1').textContent"), "Rendered content can be inspected"), "WebView2 displays the page");
        Expect(!view.CoreWebView2.Settings.AreHostObjectsAllowed && !view.CoreWebView2.Settings.IsWebMessageEnabled, "Web content receives no native application bridge");

        var cookie = view.CoreWebView2.CookieManager.CreateCookie("scope_check", "first-account", "example.invalid", "/");
        view.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
        Equal(1, Await(view.CoreWebView2.CookieManager.GetCookiesAsync("https://example.invalid"), "Test account cookie is readable").Count(item => item.Name == "scope_check"), "Test account has its own website session");
        var second = ShowBrowser(secondFolder);
        var secondView = (WebView2)second.FindName("Browser");
        Equal(0, Await(secondView.CoreWebView2.CookieManager.GetCookiesAsync("https://example.invalid"), "Other account cookies are readable").Count(item => item.Name == "scope_check"), "Website cookies do not cross accounts");
        second.Close(); first.Close();
        var closed = new AccountBrowserWindow("Test", "", UsageResetPage.Url, firstFolder);
        closed.Close();
        Expect(!Await(closed.Ready, "Closing a browser before loading settles initialization"), "Closed browser does not initialize later");
    }
}
