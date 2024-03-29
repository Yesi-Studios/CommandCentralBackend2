﻿using System;
using FluentNHibernate.Mapping;
using System.Collections.Generic;
using System.Linq;
using NHibernate.Type;

namespace CommandCentral.Entities
{
    /// <summary>
    /// Describes a single profile lock.
    /// </summary>
    public class ProfileLock
    {
        /// <summary>
        /// The maximum age, as a timespan, after which a profile lock should be considered invalid.
        /// </summary>
        public static TimeSpan MaxAge { get; } = TimeSpan.FromMinutes(20);

        #region Properties

        /// <summary>
        /// The unique GUID assigned to this Profile Lock
        /// </summary>
        public virtual Guid Id { get; set; }

        /// <summary>
        /// The person who owns this lock.
        /// </summary>
        public virtual Person Owner { get; set; }

        /// <summary>
        /// The Person whose profile is locked.
        /// </summary>
        public virtual Person LockedPerson { get; set; }

        /// <summary>
        /// The time at which this lock was submitted.
        /// </summary>
        public virtual DateTime SubmitTime { get; set; }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Returns a timespan indicating for how much longer this profile lock is valid.
        /// </summary>
        /// <returns></returns>
        public virtual TimeSpan GetTimeRemaining()
        {
            return DateTime.UtcNow.Subtract(SubmitTime);
        }

        /// <summary>
        /// Returns a booleans indicating if the current ProfileLock is valid - compares against the max age found in the config.
        /// </summary>
        /// <returns></returns>
        public virtual bool IsValid()
        {
            return DateTime.UtcNow.Subtract(SubmitTime) < MaxAge;
        }

        #endregion

        /// <summary>
        /// Maps a profile lock to the database.
        /// </summary>
        public class ProfileLockMapping : ClassMap<ProfileLock>
        {
            /// <summary>
            /// Maps a profile lock to the database.
            /// </summary>
            public ProfileLockMapping()
            {
                Id(x => x.Id).GeneratedBy.Assigned();

                References(x => x.Owner).Not.Nullable();
                References(x => x.LockedPerson).Not.Nullable().Unique();

                Map(x => x.SubmitTime).Not.Nullable().CustomType<UtcDateTimeType>();
            }
        }
    }
}
