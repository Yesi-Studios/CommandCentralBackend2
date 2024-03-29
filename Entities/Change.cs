﻿using System;
using FluentNHibernate.Mapping;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Linq;
using NHibernate.Type;
using CommandCentral.Utilities;

namespace CommandCentral.Entities
{
    /// <summary>
    /// Describes a single change
    /// </summary>
    public class Change
    {
        #region Properties

        /// <summary>
        /// The Id of this change.
        /// </summary>
        public virtual Guid Id { get; set; }

        /// <summary>
        /// The client who initiated this change.
        /// </summary>
        public virtual Person Editor { get; set; }

        /// <summary>
        /// The person who was edited.
        /// </summary>
        public virtual Person Editee { get; set; }

        /// <summary>
        /// The name of the property of the object that changed.
        /// </summary>
        public virtual string PropertyName { get; set; }

        /// <summary>
        /// The value prior to the update or change.
        /// </summary>
        public virtual string OldValue { get; set; }

        /// <summary>
        /// The new value.
        /// </summary>
        public virtual string NewValue { get; set; }

        /// <summary>
        /// The time this change was made.
        /// </summary>
        public virtual DateTime Time { get; set; }

        #endregion

        #region Overrides

        /// <summary>
        /// Casts the
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"The property '{PropertyName}' changed from '{OldValue}' to '{NewValue}'.";
        }

        /// <summary>
        /// Deep equals.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            var other = obj as Change;
            if (other == null)
                return false;

            return Object.Equals(other.Editee.Id, this.Editee.Id) &&
                   Object.Equals(other.Editor.Id, this.Editor.Id) &&
                   Object.Equals(other.Id, this.Id) &&
                   Object.Equals(other.NewValue, this.NewValue) &&
                   Object.Equals(other.OldValue, this.OldValue) &&
                   Object.Equals(other.PropertyName, this.PropertyName) &&
                   Object.Equals(other.Time, this.Time);
        }

        /// <summary>
        /// hashey codey
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;

                hash = hash * 23 + NullSafeUtilities.GetSafeHashCode(Id);
                hash = hash * 23 + NullSafeUtilities.GetSafeHashCode(Editee.Id);
                hash = hash * 23 + NullSafeUtilities.GetSafeHashCode(Editor.Id);
                hash = hash * 23 + NullSafeUtilities.GetSafeHashCode(PropertyName);
                hash = hash * 23 + NullSafeUtilities.GetSafeHashCode(OldValue);
                hash = hash * 23 + NullSafeUtilities.GetSafeHashCode(NewValue);
                hash = hash * 23 + NullSafeUtilities.GetSafeHashCode(Time);

                return hash;
            }
        }

        #endregion

        /// <summary>
        /// Maps a change to the database.
        /// </summary>
        public class ChangeMapping : ClassMap<Change>
        {
            /// <summary>
            /// Maps a change to the database.
            /// </summary>
            public ChangeMapping()
            {
                Id(x => x.Id).GeneratedBy.Assigned();

                References(x => x.Editor).Not.Nullable();
                References(x => x.Editee).Not.Nullable();

                Map(x => x.Time).Not.Nullable().CustomType<UtcDateTimeType>();
                Map(x => x.PropertyName).Not.Nullable();
                Map(x => x.OldValue);
                Map(x => x.NewValue);
            }
        }

    }
}
