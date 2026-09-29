using System;

namespace OpeningBell.Fund
{
    /// <summary>A course as offered to one employee right now: level, price, time, the gain it'd give, or why not.</summary>
    public readonly struct CourseOffer
    {
        public readonly Course Course;
        public readonly int Level;
        public readonly decimal Price;
        public readonly int Minutes;
        public readonly double Gain;
        public readonly string Blocker;

        public CourseOffer(Course course, int level, decimal price, int minutes, double gain, string blocker)
        {
            Course = course;
            Level = level;
            Price = price;
            Minutes = minutes;
            Gain = gain;
            Blocker = blocker;
        }
    }

    public sealed partial class HedgeFund
    {
        /// <summary>The next level of a course for this employee, with its full cost and duration, or why it's unavailable.</summary>
        public CourseOffer OfferCourse(Employee e, Skill skill)
        {
            Course c = TrainingCatalog.For(skill);
            int level = TrainingCatalog.LevelsTaken(e, skill) + 1;
            if (level > c.MaxLevel) return new CourseOffer(c, level, 0m, 0, 0, "Every level of this course is done.");
            decimal price = TrainingCatalog.Price(Config, skill, level);
            int minutes = TrainingCatalog.Minutes(Config, skill, level, e.Person);
            double gain = TrainingCatalog.Gain(e.Person, skill, level);
            string blocker = null;
            if (e.Former) blocker = $"{e.Name} no longer works here.";
            else if (e.CurrentTraining != null) blocker = $"{e.Name} is already on {Person.SkillName(e.CurrentTraining.Skill).ToLowerInvariant()} training.";
            else if (gain < 0.5) blocker = $"{e.Person.First} is at their ceiling in {Person.SkillName(skill).ToLowerInvariant()}.";
            else if (FreeCash < price) blocker = string.Format(C, "Costs {0}; the company has {1} free.", Money(price), Money(Math.Max(0m, FreeCash)));
            return new CourseOffer(c, level, price, minutes, gain, blocker);
        }

        /// <summary>
        /// Buys the next level of a course for one employee: paid once, now; the gain is fixed at purchase. It starts when
        /// they're at a working desk, in work hours and flat (queued until then). One course at a time, so a second click
        /// can't stack another. Error, or null.
        /// </summary>
        public string BuyTraining(Employee e, Skill skill)
        {
            CourseOffer o = OfferCourse(e, skill);
            if (o.Blocker != null) return o.Blocker;
            DateTime now = _market.Now;
            string error = Spend(now, CashKind.Training, ExpenseKind.Training, o.Price, $"{Person.SkillName(skill)} training {o.Level}: {e.Name}", e.Id);
            if (error != null) return error;
            e.Training.Add(new TrainingJob
            {
                Id = _nextJobId++, Skill = skill, Level = o.Level, Price = o.Price, Minutes = o.Minutes, Bought = now.Ticks, Gain = o.Gain,
            });
            e.Person.Note(now, $"Enrolled in {Person.SkillName(skill).ToLowerInvariant()} level {o.Level}");
            Touch();
            return null;
        }

        /// <summary>Why a bought course hasn't started (for "Queued: ..."), or null when it's running or there's none.</summary>
        public string TrainingQueueReason(Employee e)
        {
            TrainingJob j = e.CurrentTraining;
            if (j == null || e.Activity == Activity.Training) return null;
            if (e.Activity == Activity.OffDuty || e.Activity == Activity.Commuting || e.Activity == Activity.AwaitingStart || e.Activity == Activity.Arriving)
                return "Queued: starts when they're at their desk.";
            if (StationOf(e) == null) return "Queued: needs a working desk.";
            if (e.Activity == Activity.OnBreak) return "Queued: on a break.";
            if (!e.IsFlat) return "Queued: finishing their open trade first.";
            return "Queued.";
        }

        /// <summary>"Training: Market analysis 4/10 min" while studying, the queue reason while waiting, null with none.</summary>
        public string CurrentTrainingLabel(Employee e)
        {
            TrainingJob j = e.CurrentTraining;
            if (j == null) return null;
            if (e.Activity == Activity.Training)
                return string.Format(C, "Training: {0} {1:0}/{2} min", Person.SkillName(j.Skill), Math.Floor(j.MinutesDone), j.Minutes);
            return TrainingQueueReason(e);
        }

        private void TrainMinute(Employee e, DateTime t)
        {
            TrainingJob j = e.CurrentTraining;
            if (j == null) return;
            if (j.Started == 0)
            {
                j.Started = t.Ticks;
                e.Note(t, $"Started {Person.SkillName(j.Skill).ToLowerInvariant()} training (level {j.Level}).");
            }
            j.MinutesDone += 1;
            if (e.Today != null) e.Today.MinutesTraining++;
            if (j.MinutesDone < j.Minutes) return;
            j.Finished = t.Ticks;
            int i = (int)j.Skill;
            double before = e.Person.Skills[i];
            e.Person.Skills[i] = Math.Min(e.Person.Caps[i], before + j.Gain);
            e.Person.Note(t, string.Format(C, "Completed {0} level {1}: {2:0} → {3:0}", Person.SkillName(j.Skill).ToLowerInvariant(), j.Level, before, e.Person.Skills[i]));
            e.Feel(t, "Recent training", 3);
            string extra = j.Skill == Skill.Analysis
                ? string.Format(C, " Estimated win rate under reference conditions: {0:0.0}%.", PeopleFactory.EstimatedWinRate(e.Person) * 100)
                : "";
            Notify(NoticeLevel.Important, "Training completed", string.Format(C, "{0} finished {1} level {2} ({3:0} → {4:0}).{5}",
                e.Name, Person.SkillName(j.Skill).ToLowerInvariant(), j.Level, before, e.Person.Skills[i], extra), e.Id);
            Touch();
        }
    }
}
