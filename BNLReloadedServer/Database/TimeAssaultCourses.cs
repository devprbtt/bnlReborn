using BNLReloadedServer.BaseTypes;
using MatchType = BNLReloadedServer.BaseTypes.MatchType;

namespace BNLReloadedServer.Database;

public static class TimeAssaultCourses
{
    // Null is only the legacy "featured course" request. An explicit unknown key must never fall back.
    public static TimeTrialCourse? Select(IEnumerable<TimeTrialCourse>? courses, Key? requested)
    {
        if (courses == null) return null;
        if (!requested.HasValue) return courses.FirstOrDefault();
        var matches = courses.Where(c => c.Map == requested.Value).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static bool TryLoad(Key? requested, out TimeTrialCourse? course, out MapData? map)
    {
        course = Select(CatalogueHelper.GlobalLogic.TimeTrial?.Courses, requested);
        map = null;
        if (course == null || course.Hero.GetCard<CardUnit>() == null || course.Map.GetCard<CardMap>()?.Data?.Match != MatchType.TimeTrial) return false;
        map = Databases.MapDatabase.LoadMapData(course.Map);
        return map?.Match == MatchType.TimeTrial;
    }
}
