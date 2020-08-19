﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Outlook = Microsoft.Office.Interop.Outlook;
using System.Diagnostics;

namespace ChimeOutlookHelper
{
  static class OutlookHelper
  {

    private const int DEFAULT_SEARCH_HOURS = 1;

    public static List<Outlook.Folder> GetCalendars()
    {
      var outlook = new Outlook.Application();
      var stores = outlook.Session.Stores;

      var folders = new List<Outlook.Folder>();

      foreach (Outlook.Store store in stores)
      {
        // amazingly this is possible...
        if (store == null)
          continue;

        try
        {
          // ignore public folders (causes slow Exchange calls, and we don't have a use case
          // for interactions with those)
          if (store.ExchangeStoreType == Outlook.OlExchangeStoreType.olExchangePublicFolder)
            continue;

          var folder = (Outlook.Folder)store.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderCalendar);
          System.Diagnostics.Debug.WriteLine(folder.Name);

          folders.Add(folder);
        }
        catch (Exception e)
        {
          // Not every root folder has a calendar (for example, Public folders), so this exception can be ignored
          Debug.WriteLine("Failed to get Calendar:\n" + e);
        }
      }

      if (folders.Count > 0)
        return folders;

      throw new InvalidOperationException("Couldn't find a Calendar in the current Outlook installation");
    }

    public static List<Outlook.AppointmentItem> GetAppointmentsAroundNow(Outlook.Folder calendar, int hours = DEFAULT_SEARCH_HOURS)
    {
      try
      {
        var now = DateTime.Now;

        // align to the top of the hour since most meetings are aligned to the hour boundary
        var start = now.Subtract(new TimeSpan(hours, now.Minute, 0));

        // ignore the current hour (so the buffer applies back and forward equally)
        // don't worry about seconds, as :00 will still match
        var end = now.Add(new TimeSpan(hours, 60 - now.Minute, 0));

        return GetAppointmentsInRange(calendar, start, end);
      }
      catch (Exception e)
      {
        Debug.WriteLine($"Swallowed Exception in GetAppointmentsAroundNow: {e}");

        return null;
      }
    }

    public static List<Outlook.AppointmentItem> GetAppointmentsInRange(Outlook.Folder folder, DateTime start, DateTime end, bool includeRecurrences = true)
    {
      // we originally combined the filters with an "OR" but it would seemingly miss some random meetings for no apparent reason
      // explored whether or not there was API caching but couldn't find any. Splitting it out into two queries seems to solve the 
      // issue, even if the code is a little more weird.

      // find all meetings that start within the period (regardless of when they end specifically)
      var filter = $"[Start] >= '{start.ToString("g")}' AND [Start] <= '{end.ToString("g")}'";

      var rv = RestrictItems(folder, filter, includeRecurrences);

      // find meetings that are in-progress during the period (for example, all day events that start before the period but end
      // during or after it
      filter = $"[Start] < '{start.ToString("g")}' AND [End] >= '{start.ToString("g")}'";

      rv.AddRange(RestrictItems(folder, filter, includeRecurrences));

      return rv;
    }

    public static List<Outlook.AppointmentItem> RestrictItems(Outlook.Folder folder, string filter, bool includeRecurrences)
    {
      var rv = new List<Outlook.AppointmentItem>();

      var items = folder.Items;
      items.IncludeRecurrences = includeRecurrences;
      items.Sort("[Start]", Type.Missing);

      var restricted = items.Restrict(filter);

      foreach (Outlook.AppointmentItem item in restricted)
      {
        rv.Add(item);

        Debug.WriteLine("++: " + item.Start + " -> " + item.End + ": " + item.Subject);
      }

      return rv;
    }

  }
}
