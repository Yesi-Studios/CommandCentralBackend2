﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentNHibernate.Mapping;
using FluentValidation.Results;

namespace CommandCentral.Entities.ReferenceLists
{
    public class DutyStatus : ReferenceListItemBase
    {
        public class DutyStatusMapping : ClassMap<DutyStatus>
        {
            public DutyStatusMapping()
            {
                Id(x => x.Id).GeneratedBy.Assigned();

                Map(x => x.Value).Not.Nullable().Unique();
                Map(x => x.Description);

                Cache.ReadWrite();
            }
        }

    }
}
