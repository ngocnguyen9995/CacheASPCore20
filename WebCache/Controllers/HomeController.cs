﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using WebCache.Models;

namespace WebCache.Controllers
{
    
    public class HomeController : Controller
    {
        private IMemoryCache _cache;

        #region snippet_ctor
        public HomeController(IMemoryCache memoryCache)
        {
            _cache = memoryCache;
        }
        #endregion

        public IActionResult Index()
        {
            return RedirectToAction("CacheGet");
        }

        public IActionResult CacheTryGetValueSet()
        {
            DateTime cacheEntry;
            if (! _cache.TryGetValue(CacheKeys.Entry, out cacheEntry))
            {
                cacheEntry = DateTime.Now;
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromSeconds(3));
                _cache.Set(CacheKeys.Entry, cacheEntry, cacheEntryOptions);
            }
            return ViewComponent("Cache", cacheEntry);
        }

        public IActionResult CacheGet()
        {
            var cacheEntry = _cache.Get<DateTime?>(CacheKeys.Entry);
            return View("Cache", cacheEntry);
        }

        public IActionResult CacheGetOrCrate()
        {
            var cacheEntry = _cache.GetOrCreate(CacheKeys.Entry, entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromSeconds(3);
                return DateTime.Now;
            });

            return View("Cache", cacheEntry);
        }

        public async Task<IActionResult> CacheGetOrCreateAsync()
        {
            var cacheEntry = await
                _cache.GetOrCreateAsync(CacheKeys.Entry, entry =>
                {
                    entry.SlidingExpiration = TimeSpan.FromSeconds(3);
                    return Task.FromResult(DateTime.Now);
                });
            return View("Cache", cacheEntry);

        }

        public IActionResult CacheRemove()
        {
            _cache.Remove(CacheKeys.Entry);
            return RedirectToAction("CacheGet");
        }

        public IActionResult CreateCallbackEntry()
        {
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetPriority(CacheItemPriority.NeverRemove)
                .RegisterPostEvictionCallback(callback: EvictionCallback, state: this);
            _cache.Set(CacheKeys.CallbackEntry, DateTime.Now, cacheEntryOptions);
            return RedirectToAction("GetCallbackEntry");
        }

        public IActionResult GetCallbackEntry()
        {
            return View("Callback", new CallbackViewModel
            {
                CachedTime = _cache.Get<DateTime?>(CacheKeys.CallbackEntry),
                Message = _cache.Get<string>(CacheKeys.CallbackMessage)
            });
        }

        public IActionResult RemoveCallbackEntry()
        {
            _cache.Remove(CacheKeys.CallbackEntry);
            return RedirectToAction("GetCallbackEntry");
        }

        public static void EvictionCallback(object key, object value, EvictionReason reason, object state)
        {
            var message = $"Entry was evicted. Reason: {reason}.";
            ((HomeController)state)._cache.Set(CacheKeys.CallbackMessage, message);
        }

        public IActionResult CreateDependentEntries()
        {
            var cts = new CancellationTokenSource();
            _cache.Set(CacheKeys.DependentCTS, cts);
            using (var entry = _cache.CreateEntry(CacheKeys.Parent))
            {
                // expire this entry if dependent expires
                entry.Value = DateTime.Now;
                entry.RegisterPostEvictionCallback(DependentEvictionCallback, this);
                _cache.Set(CacheKeys.Child, DateTime.Now, new CancellationChangeToken(cts.Token));
            }
            return RedirectToAction("GetDependentEntries");
        }

        public IActionResult GetDependentEntries()
        {
            return View("Dependent", new DependentViewModel
            {
                ParentCachedTime = _cache.Get<DateTime?>(CacheKeys.Parent),
                ChildCachedTime = _cache.Get<DateTime?>(CacheKeys.Child),
                Message = _cache.Get<string>(CacheKeys.DependentMessage)
            }); 
        }

        public IActionResult RemoveChildEntry()
        {
            _cache.Get<CancellationTokenSource>(CacheKeys.DependentCTS).Cancel();
            return RedirectToAction("GetDependentEntries");
        }

        private static void DependentEvictionCallback(object key, object value, EvictionReason reason, object state)
        {
            var message = $"Parent entry evicted. Reason: {reason}";
            ((HomeController)state)._cache.Set(CacheKeys.DependentMessage, message);
        }

        public IActionResult CancelTest()
        {
            var cachedVal = DateTime.Now.Second.ToString();
            CancellationTokenSource cts = new CancellationTokenSource();
            _cache.Set<CancellationTokenSource>(CacheKeys.CancelTokenSource, cts);

            // Don't use prev message
            _cache.Remove(CacheKeys.CancelMsg);
            _cache.Set(CacheKeys.Ticks, cachedVal, new MemoryCacheEntryOptions()
                .AddExpirationToken(new CancellationChangeToken(cts.Token))
                .RegisterPostEvictionCallback(
                    (key, value, reason, substate) =>
                    {
                        var cm = $"'{key}':'{value}' was evicted because: {reason}";
                        _cache.Set<string>(CacheKeys.CancelMsg, cm);
                    }
                ));
            return RedirectToAction("CheckCancel");
        }

        public IActionResult CheckCancel(int? id = 0)
        {
            if (id > 0)
            {
                CancellationTokenSource cts = _cache.Get<CancellationTokenSource>(CacheKeys.CancelTokenSource);
                cts.CancelAfter(100);
            }
            ViewData["CachedTime"] = _cache.Get<string>(CacheKeys.Ticks);
            ViewData["Message"] = _cache.Get<string>(CacheKeys.CancelMsg);

            return View();
        }
    }

}
