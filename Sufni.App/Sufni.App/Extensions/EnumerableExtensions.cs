using System;
using System.Collections.Generic;

namespace Sufni.App.Extensions;

// source: https://markheath.net/post/suppress-exceptions-linq
public static class EnumerableExtensions
{
    public static IEnumerable<TResult> TrySelect<TSource, TResult, TException>(this IEnumerable<TSource> source,
        Func<TSource, TResult> selector,
        Action<TSource, TException>? exceptionAction)
        where TException : Exception
    {
        foreach (var s in source)
        {
            var result = default(TResult);
            var success = false;
            try
            {
                result = selector(s);
                success = true;
            }
            catch (TException ex)
            {
                exceptionAction?.Invoke(s, ex);
            }
            if (success)
            {
                // n.b. can't yield return inside a try block    
                yield return result!;
            }
        }
    }
}