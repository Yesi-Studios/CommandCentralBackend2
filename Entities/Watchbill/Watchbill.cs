﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentNHibernate.Mapping;
using FluentValidation;
using CommandCentral.Entities.ReferenceLists.Watchbill;
using System.Globalization;
using NHibernate;
using NHibernate.Type;
using CommandCentral.Authorization;
using CommandCentral.Utilities.Types;
using CommandCentral.Framework.Data;
using CommandCentral.Utilities;

namespace CommandCentral.Entities.Watchbill
{
    /// <summary>
    /// Describes a single watchbill, which is a collection of watch days, shifts in those days, and inputs.
    /// </summary>
    public class Watchbill
    {

        #region Properties

        /// <summary>
        /// The unique Id of this watchbill.
        /// </summary>
        public virtual Guid Id { get; set; }

        /// <summary>
        /// The free text title of this watchbill.
        /// </summary>
        public virtual string Title { get; set; }

        /// <summary>
        /// The person who created this watchbill.  This is expected to often be the command watchbill coordinator.
        /// </summary>
        public virtual Person CreatedBy { get; set; }

        /// <summary>
        /// Represents the current state of the watchbill.  Different states should trigger different actions.
        /// </summary>
        public virtual WatchbillStatus CurrentState { get; set; }

        /// <summary>
        /// Indicates the last time the state of this watchbill was changed.
        /// </summary>
        public virtual DateTime LastStateChange { get; set; }

        /// <summary>
        /// Contains a reference to the person who caused the last state change.
        /// </summary>
        public virtual Person LastStateChangedBy { get; set; }

        /// <summary>
        /// The list of all watch shifts that exist on this watchbill.
        /// </summary>
        public virtual IList<WatchShift> WatchShifts { get; set; } = new List<WatchShift>();

        /// <summary>
        /// The list of all watch inputs on this watchbill.
        /// </summary>
        public virtual IList<WatchInput> WatchInputs { get; set; } = new List<WatchInput>();

        /// <summary>
        /// The min and max dates of the watchbill.
        /// </summary>
        public virtual TimeRange Range { get; set; }

        /// <summary>
        /// The collection of requirements.  This is how we know who needs to provide inputs and who is available to be on this watchbill.
        /// </summary>
        public virtual IList<WatchInputRequirement> InputRequirements { get; set; } = new List<WatchInputRequirement>();

        /// <summary>
        /// The command at which this watchbill was created.
        /// </summary>
        public virtual ReferenceLists.Command Command { get; set; }

        /// <summary>
        /// This is how the watchbill knows the pool of people to use when assigning inputs, and assigning watches.  
        /// <para />
        /// The eligibilty group also determines the type of watchbill.
        /// </summary>
        public virtual WatchEligibilityGroup EligibilityGroup { get; set; }

        #endregion

        #region ctors

        /// <summary>
        /// Creates a new watchbill, setting all collection to empty.
        /// </summary>
        public Watchbill()
        {
        }

        #endregion

        #region Methods

        /// <summary>
        /// This method is responsible for advancing the watchbill into different states.  
        /// At each state, different actions must be taken.
        /// </summary>
        /// <param name="desiredState"></param>
        /// <param name="setTime"></param>
        /// <param name="person"></param>
        /// <param name="session"></param>
        public virtual void SetState(WatchbillStatus desiredState, DateTime setTime, Person person, ISession session)
        {
            //Don't allow same changes.
            if (this.CurrentState == desiredState)
            {
                throw new Exception("Can't set the state to its same value.");
            }

            //If we set a watchbill's state to initial, then remove all the assignments from it, 
            //leaving a watchbill with only its days and shifts.
            if (desiredState == ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Initial"))
            {
                this.InputRequirements.Clear();
                this.WatchInputs.Clear();

                foreach (var shift in this.WatchShifts)
                {
                    if (shift.WatchAssignment != null)
                    {
                        session.Delete(shift.WatchAssignment);
                        shift.WatchAssignment = null;
                    }
                }

                //We also need to remove the job.
                if (FluentScheduler.JobManager.RunningSchedules.Any(x => x.Name == this.Id.ToString()))
                    FluentScheduler.JobManager.RemoveJob(this.Id.ToString());
            }
            //Inform all the people who need to provide inputs along with all the people who are in its chain of command.
            else if (desiredState == ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Open for Inputs"))
            {
                if (this.CurrentState == null || this.CurrentState != ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Initial"))
                    throw new Exception("You may not move to the open for inputs state from anything other than the initial state.");

                foreach (var elPerson in this.EligibilityGroup.EligiblePersons)
                {
                    this.InputRequirements.Add(new WatchInputRequirement
                    {
                        Id = Guid.NewGuid(),
                        Person = elPerson
                    });
                }

                var emailAddressesByPerson = this.EligibilityGroup.EligiblePersons
                    .Select(x => new KeyValuePair<string, List<System.Net.Mail.MailAddress>>(x.ToString(), x.EmailAddresses.Where(y => y.IsPreferred).Select(y => new System.Net.Mail.MailAddress(y.Address, x.ToString())).ToList())).ToList();

                //Start a new task to send all the emails.
                Task.Run(() =>
                {
                    foreach (var group in emailAddressesByPerson)
                    {
                        var model = new Email.Models.WatchbillInputRequiredEmailModel { FriendlyName = group.Key, Watchbill = this.Title };

                        Email.EmailInterface.CCEmailMessage
                            .CreateDefault()
                            .To(group.Value)
                            .CC(Email.EmailInterface.CCEmailMessage.DeveloperAddress)
                            .Subject("Watchbill Inputs Required")
                            .HTMLAlternateViewUsingTemplateFromEmbedded("CommandCentral.Email.Templates.WatchbillInputRequired_HTML.html", model)
                            .SendWithRetryAndFailure(TimeSpan.FromSeconds(1));
                    }

                }).ConfigureAwait(false);


                //We now also need to load all persons in the watchbill's chain of command
                var groups = new Authorization.Groups.PermissionGroup[] { new Authorization.Groups.Definitions.CommandQuarterdeckWatchbill(),
                                    new Authorization.Groups.Definitions.DepartmentQuarterdeckWatchbill(),
                                    new Authorization.Groups.Definitions.DivisionQuarterdeckWatchbill() }
                    .Select(x => x.GroupName)
                    .ToList();

                using (var internalSession = DataAccess.DataProvider.CreateStatefulSession())
                using (var transaction = internalSession.BeginTransaction())
                {

                    try
                    {
                        var queryString = "from Person as person where (";
                        for (var x = 0; x < groups.Count; x++)
                        {
                            queryString += " '{0}' in elements(person.{1}) ".With(groups[x], 
                                PropertySelector.SelectPropertyFrom<Person>(y => y.PermissionGroupNames).Name);
                            if (x + 1 != groups.Count)
                                queryString += " or ";
                        }
                        queryString += " ) and person.Command = :command";
                        var persons = internalSession.CreateQuery(queryString)
                            .SetParameter("command", this.Command)
                            .List<Person>();

                        //Now with these people who are the duty holders.
                        var collateralEmailAddresses = persons.Select(x => 
                                    x.EmailAddresses.Where(y => y.IsPreferred).Select(y => new System.Net.Mail.MailAddress(y.Address, x.ToString())).ToList()).ToList();

                        var uniqueWatchQuals = this.WatchShifts.SelectMany(x => x.ShiftType.RequiredWatchQualifications).Distinct();
                        var personNamesWithoutWatchQualifications = this.EligibilityGroup.EligiblePersons.Where(p => !p.WatchQualifications.Any(qual => uniqueWatchQuals.Contains(qual))).Select(x => x.ToString()).ToList();

                        Task.Run(() =>
                        {
                            var model = new Email.Models.WatchbillOpenForInputsEmailModel { WatchbillTitle = this.Title, NotQualledPersonsFriendlyNames = personNamesWithoutWatchQualifications };

                            foreach (var addressGroup in collateralEmailAddresses)
                            {
                                Email.EmailInterface.CCEmailMessage
                                    .CreateDefault()
                                    .To(addressGroup)
                                    .CC(Email.EmailInterface.CCEmailMessage.DeveloperAddress)
                                    .Subject("Watchbill Open For Inputs")
                                    .HTMLAlternateViewUsingTemplateFromEmbedded("CommandCentral.Email.Templates.WatchbillOpenForInputs_HTML.html", model)
                                    .SendWithRetryAndFailure(TimeSpan.FromSeconds(1));

                            }

                        }).ConfigureAwait(false);

                        
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }

                //We now need to register the job that will send emails every day to alert people to the inputs they are responsible for.
                FluentScheduler.JobManager.AddJob(() => SendWatchInputRequirementsAlertEmail(this.Id), s => s.WithName(this.Id.ToString()).ToRunNow().AndEvery(1).Days().At(4, 0));
                
            }
            //Inform everyone in the chain of command that the watchbill is closed for inputs.
            else if (desiredState == ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Closed for Inputs"))
            {
                if (this.CurrentState == null || this.CurrentState != ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Open for Inputs"))
                    throw new Exception("You may not move to the closed for inputs state from anything other than the open for inputs state.");

                //We now also need to load all persons in the watchbill's chain of command.
                var groups = new Authorization.Groups.PermissionGroup[] { new Authorization.Groups.Definitions.CommandQuarterdeckWatchbill(),
                                    new Authorization.Groups.Definitions.DepartmentQuarterdeckWatchbill(),
                                    new Authorization.Groups.Definitions.DivisionQuarterdeckWatchbill() }
                    .Select(x => x.GroupName)
                    .ToList();

                using (var internalSession = DataAccess.DataProvider.CreateStatefulSession())
                using (var transaction = internalSession.BeginTransaction())
                {

                    try
                    {
                        var queryString = "from Person as person where (";
                        for (var x = 0; x < groups.Count; x++)
                        {
                            queryString += " '{0}' in elements(person.{1}) ".With(groups[x],
                                PropertySelector.SelectPropertyFrom<Person>(y => y.PermissionGroupNames).Name);
                            if (x + 1 != groups.Count)
                                queryString += " or ";
                        }
                        queryString += " ) and person.Command = :command";
                        var persons = internalSession.CreateQuery(queryString)
                            .SetParameter("command", this.Command)
                            .List<Person>();

                        //Now with these people who are the duty holders.
                        var collateralEmailAddresses = persons.Select(x =>
                                    x.EmailAddresses.Where(y => y.IsPreferred).Select(y => new System.Net.Mail.MailAddress(y.Address, x.ToString())).ToList()).ToList();

                        Task.Run(() =>
                        {
                            var model = new Email.Models.WatchbillClosedForInputsEmailModel { Watchbill = this.Title };

                            foreach (var addressGroup in collateralEmailAddresses)
                            {
                                Email.EmailInterface.CCEmailMessage
                                    .CreateDefault()
                                    .To(addressGroup)
                                    .CC(Email.EmailInterface.CCEmailMessage.DeveloperAddress)
                                    .Subject("Watchbill Closed For Inputs")
                                    .HTMLAlternateViewUsingTemplateFromEmbedded("CommandCentral.Email.Templates.WatchbillClosedForInputs_HTML.html", model)
                                    .SendWithRetryAndFailure(TimeSpan.FromSeconds(1));
                            }

                        }).ConfigureAwait(false);


                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }

                if (FluentScheduler.JobManager.RunningSchedules.Any(x => x.Name == this.Id.ToString()))
                    FluentScheduler.JobManager.RemoveJob(this.Id.ToString());
            }
            //Make sure there are assignments for each shift.  
            //Inform the chain of command that the watchbill is open for review.
            else if (desiredState == ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Under Review"))
            {
                if (this.CurrentState == null || this.CurrentState != ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Closed for Inputs"))
                    throw new Exception("You may not move to the under review state from anything other than the closed for inputs state.");

                if (!this.WatchShifts.All(y => y.WatchAssignment != null))
                {
                    throw new Exception("A watchbill may not move into the 'Under Review' state unless the all watch shifts have been assigned.");
                }
                
                //We now also need to load all persons in the watchbill's chain of command.
                var groups = new Authorization.Groups.PermissionGroup[] { new Authorization.Groups.Definitions.CommandQuarterdeckWatchbill(),
                                    new Authorization.Groups.Definitions.DepartmentQuarterdeckWatchbill(),
                                    new Authorization.Groups.Definitions.DivisionQuarterdeckWatchbill() }
                    .Select(x => x.GroupName)
                    .ToList();

                using (var internalSession = DataAccess.DataProvider.CreateStatefulSession())
                using (var transaction = internalSession.BeginTransaction())
                {

                    try
                    {
                        var queryString = "from Person as person where (";
                        for (var x = 0; x < groups.Count; x++)
                        {
                            queryString += " '{0}' in elements(person.{1}) ".With(groups[x],
                                PropertySelector.SelectPropertyFrom<Person>(y => y.PermissionGroupNames).Name);
                            if (x + 1 != groups.Count)
                                queryString += " or ";
                        }
                        queryString += " ) and person.Command = :command";
                        var persons = internalSession.CreateQuery(queryString)
                            .SetParameter("command", this.Command)
                            .List<Person>();

                        //Now with these people who are the duty holders.
                        var collateralEmailAddresses = persons.Select(x =>
                                    x.EmailAddresses.Where(y => y.IsPreferred).Select(y => new System.Net.Mail.MailAddress(y.Address, x.ToString())).ToList()).ToList();

                        Task.Run(() =>
                        {
                            var model = new Email.Models.WatchbillUnderReviewEmailModel { Watchbill = this.Title };

                            foreach (var addressGroup in collateralEmailAddresses)
                            {
                                Email.EmailInterface.CCEmailMessage
                                    .CreateDefault()
                                    .To(addressGroup)
                                    .CC(Email.EmailInterface.CCEmailMessage.DeveloperAddress)
                                    .Subject("Watchbill Under Review")
                                    .HTMLAlternateViewUsingTemplateFromEmbedded("CommandCentral.Email.Templates.WatchbillUnderReview_HTML.html", model)
                                    .SendWithRetryAndFailure(TimeSpan.FromSeconds(1));
                            }
                        }).ConfigureAwait(false);

                                

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            //Move the watchbill into its published state, tell everyone who has watch which watches they have.
            //Tell the chain of command the watchbill is published.
            else if (desiredState == ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Published"))
            {
                if (this.CurrentState == null || this.CurrentState != ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Under Review"))
                    throw new Exception("You may not move to the published state from anything other than the under review state.");

                //Let's send an email to each person who is on watch, informing them of their watches.
                var assignmentsByPerson = this.WatchShifts.Select(x => x.WatchAssignment)
                    .GroupBy(x => x.PersonAssigned);

                foreach (var assignments in assignmentsByPerson)
                {
                    var model = new Email.Models.WatchAssignedEmailModel { FriendlyName = assignments.Key.ToString(), WatchAssignments = assignments.ToList(), Watchbill = this.Title };

                    var emailAddresses = assignments.Key.EmailAddresses.Where(x => x.IsPreferred).Select(x => new System.Net.Mail.MailAddress(x.Address, assignments.Key.ToString())).ToList();

                    Task.Run(() =>
                    {
                        Email.EmailInterface.CCEmailMessage
                            .CreateDefault()
                            .To(emailAddresses)
                            .CC(Email.EmailInterface.CCEmailMessage.DeveloperAddress)
                            .Subject("Watch Assigned")
                            .HTMLAlternateViewUsingTemplateFromEmbedded("CommandCentral.Email.Templates.WatchAssigned_HTML.html", model)
                            .SendWithRetryAndFailure(TimeSpan.FromSeconds(1));
                    }).ConfigureAwait(false);
                }

                //Let's send emails to all the coordinators.
                //We now also need to load all persons in the watchbill's chain of command.
                var groups = new Authorization.Groups.PermissionGroup[] { new Authorization.Groups.Definitions.CommandQuarterdeckWatchbill(),
                                    new Authorization.Groups.Definitions.DepartmentQuarterdeckWatchbill(),
                                    new Authorization.Groups.Definitions.DivisionQuarterdeckWatchbill() }
                    .Select(x => x.GroupName)
                    .ToList();

                using (var internalSession = DataAccess.DataProvider.CreateStatefulSession())
                using (var transaction = internalSession.BeginTransaction())
                {

                    try
                    {
                        var queryString = "from Person as person where (";
                        for (var x = 0; x < groups.Count; x++)
                        {
                            queryString += " '{0}' in elements(person.{1}) ".With(groups[x],
                                PropertySelector.SelectPropertyFrom<Person>(y => y.PermissionGroupNames).Name);
                            if (x + 1 != groups.Count)
                                queryString += " or ";
                        }
                        queryString += " ) and person.Command = :command";
                        var persons = internalSession.CreateQuery(queryString)
                            .SetParameter("command", this.Command)
                            .List<Person>();

                        //Now with these people who are the duty holders, get their preferred email addresses.
                        var collateralEmailAddresses = persons.Select(x =>
                                    x.EmailAddresses.Where(y => y.IsPreferred).Select(y => new System.Net.Mail.MailAddress(y.Address, x.ToString())).ToList()).ToList();

                        Task.Run(() =>
                        {
                            var model = new Email.Models.WatchbillPublishedEmailModel { Watchbill = this.Title };

                            foreach (var addressGroup in collateralEmailAddresses)
                            {
                                Email.EmailInterface.CCEmailMessage
                                    .CreateDefault()
                                    .To(addressGroup)
                                    .CC(Email.EmailInterface.CCEmailMessage.DeveloperAddress)
                                    .Subject("Watchbill Published")
                                    .HTMLAlternateViewUsingTemplateFromEmbedded("CommandCentral.Email.Templates.WatchbillPublished_HTML.html", model)
                                    .SendWithRetryAndFailure(TimeSpan.FromSeconds(1));
                            }

                        }).ConfigureAwait(false);

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            else
            {
                throw new NotImplementedException("Not implemented default case in the set watchbill state method.");
            }

            this.CurrentState = desiredState;
            this.LastStateChange = setTime;
            this.LastStateChangedBy = person;
        }

        /// <summary>
        /// Populates the current watchbill:
        /// 
        /// First we group all the shifts by their type, then we look at all people that are available for that watch.  
        /// We then determine how many people each department is responsible for supplying based as a percentage of the total people.
        /// This causes some shifts not to get assigned so we assign those using the Hamilton assignment method.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="dateTime"></param>
        public virtual void PopulateWatchbill(Person client, DateTime dateTime)
        {
            if (client == null)
                throw new ArgumentNullException("client");

            //First we need to know how many shifts of each type are in this watchbill.
            //And we need to know how many eligible people in each department there are.
            var shuffledShiftsByType = this.WatchShifts.Shuffle().GroupBy(x => x.ShiftType);

            foreach (var shiftGroup in shuffledShiftsByType)
            {
                var remainingShifts = new List<WatchShift>(shiftGroup.OrderByDescending(x => x.Points));

                //Get all persons from the el group who have all the required watch qualifications for the current watch type.
                var personsByDepartment = this.EligibilityGroup.EligiblePersons
                    .Where(person => shiftGroup.Key.RequiredWatchQualifications.All(watchQual => person.WatchQualifications.Contains(watchQual)))
                    .Shuffle()
                    .GroupBy(person => person.Department);

                var totalPersonsWithQuals = personsByDepartment.Select(x => x.Count()).Sum(x => x);

                var assignedShiftsByDepartment = new Dictionary<ReferenceLists.Department, double>();

                var assignablePersonsByDepartment = personsByDepartment.Select(x =>
                {
                    return new KeyValuePair<ReferenceLists.Department, ConditionalForeverList<Person>>(x.Key, new ConditionalForeverList<Person>(x.ToList().OrderBy(person =>
                    {
                        double points = person.WatchAssignments.Where(z => z.CurrentState == ReferenceLists.ReferenceListHelper<WatchAssignmentState>.Find("Completed")).Sum(z =>
                        {
                            int totalMonths = (int)Math.Round(DateTime.UtcNow.Subtract(z.WatchShift.Range.Start).TotalDays / (365.2425 / 12));

                            return z.WatchShift.Points / (Math.Pow(1.35, totalMonths) + -1);
                        });

                        return points;
                    })));
                }).ToDictionary(x => x.Key, x => x.Value);

                foreach (var personsGroup in personsByDepartment)
                {
                    //It's important to point out here that the assigned shifts will most likely not fall out as a perfect integer.
                    //We'll handle remaining shifts later.  For now, we just need to assign the whole number value of shifts.
                    var assignedShifts = (double)shiftGroup.Count() * ((double)personsGroup.Count() / (double)totalPersonsWithQuals);
                    assignedShiftsByDepartment.Add(personsGroup.Key, assignedShifts);

                    //From our list of shifts, take as many as we're supposed to assign.
                    var shiftsForThisGroup = remainingShifts.Take((int)assignedShifts).ToList();

                    for (int x = 0; x < shiftsForThisGroup.Count; x++)
                    {
                        //Ok, since we're going to assign it, we can go ahead and remove it.
                        remainingShifts.Remove(shiftsForThisGroup[x]);

                        //Determine who is about to stand this watch.
                        if (!assignablePersonsByDepartment[personsGroup.Key].TryNext(person =>
                        {
                            if (this.WatchInputs.Any(input => input.IsConfirmed && 
                                input.Person.Id == person.Id && 
                                new Itenso.TimePeriod.TimeRange(input.Range.Start, input.Range.End, true)
                                .OverlapsWith(new Itenso.TimePeriod.TimeRange(shiftsForThisGroup[x].Range.Start, shiftsForThisGroup[x].Range.End, true))))
                                return false;

                            if (person.DateOfArrival.HasValue && this.Range.Start < person.DateOfArrival.Value.AddMonths(1))
                                return false;

                            if (person.EAOS.HasValue && this.Range.Start < person.EAOS.Value.AddMonths(-1))
                                return false;

                            if (person.DateOfBirth.HasValue && new Itenso.TimePeriod.TimeRange(shiftsForThisGroup[x].Range.Start, shiftsForThisGroup[x].Range.End).HasInside(person.DateOfBirth.Value.Date))
                                return false;

                            return true;

                        }, out Person personToAssign))
                            throw new CommandCentralException("Department {0} had no person that could stand shift {1}.".With(personsGroup.Key, shiftsForThisGroup[x]), ErrorTypes.Validation);

                        //Create the watch assignment.
                        shiftsForThisGroup[x].WatchAssignment = new WatchAssignment
                        {
                            AssignedBy = client,
                            CurrentState = ReferenceLists.ReferenceListHelper<WatchAssignmentState>.Find("Assigned"),
                            DateAssigned = dateTime,
                            Id = Guid.NewGuid(),
                            PersonAssigned = personToAssign,
                            WatchShift = shiftsForThisGroup[x]
                        };
                    }
                }

                //At this step, we run into a bit of a problem.  Because the assigned shifts don't come out as perfect integers, we'll have some shifts left over.
                //I chose to use the Hamilton assignment method with the Hare quota here in order to distribute the rest of the shifts.
                //https://en.wikipedia.org/wiki/Largest_remainder_method
                var finalAssignments = assignedShiftsByDepartment.OrderByDescending(x => x.Value - Math.Truncate(x.Value)).ToList();
                foreach (var shift in remainingShifts)
                {
                    for (int x = 0; x < finalAssignments.Count; x++)
                    {
                        if (assignablePersonsByDepartment.Any() && assignablePersonsByDepartment[finalAssignments[x].Key].TryNext(person =>
                        {
                            if (this.WatchInputs.Any(input => input.IsConfirmed &&
                                input.Person.Id == person.Id &&
                                new Itenso.TimePeriod.TimeRange(input.Range.Start, input.Range.End, true)
                                    .OverlapsWith(new Itenso.TimePeriod.TimeRange(shift.Range.Start, shift.Range.End, true))))
                                return false;

                            if (person.DateOfArrival.HasValue && this.Range.Start < person.DateOfArrival.Value.AddMonths(1))
                                return false;

                            if (person.EAOS.HasValue && this.Range.Start < person.EAOS.Value.AddMonths(-1))
                                return false;

                            if (person.DateOfBirth.HasValue && new Itenso.TimePeriod.TimeRange(shift.Range.Start, shift.Range.End).HasInside(person.DateOfBirth.Value.Date))
                                return false;

                            return true;
                        }, out Person personToAssign))
                        {
                            shift.WatchAssignment = new WatchAssignment
                            {
                                AssignedBy = client,
                                CurrentState = ReferenceLists.ReferenceListHelper<WatchAssignmentState>.Find("Assigned"),
                                DateAssigned = dateTime,
                                Id = Guid.NewGuid(),
                                PersonAssigned = personToAssign,
                                WatchShift = shift
                            };

                            finalAssignments.RemoveAt(x);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns all those input requirements a person is responsible for.  Meaning those requirements that are in a person's chain of command.
        /// </summary>
        /// <param name="person"></param>
        /// <returns></returns>
        public virtual IEnumerable<WatchInputRequirement> GetInputRequirementsPersonIsResponsibleFor(Person person)
        {
            if (!person.PermissionGroupNames.Any())
                return new List<WatchInputRequirement>();

            var resolvedPermissions = person.ResolvePermissions(null);

            var highestLevelForWatchbill = resolvedPermissions.HighestLevels[this.EligibilityGroup.OwningChainOfCommand];

            if (highestLevelForWatchbill == ChainOfCommandLevels.None)
                return new List<WatchInputRequirement>();

            switch (highestLevelForWatchbill)
            {
                case ChainOfCommandLevels.Command:
                    {
                        return this.InputRequirements.Where(x => x.Person.IsInSameCommandAs(person));
                    }
                case ChainOfCommandLevels.Department:
                    {
                        return this.InputRequirements.Where(x => x.Person.IsInSameDepartmentAs(person));
                    }
                case ChainOfCommandLevels.Division:
                    {
                        return this.InputRequirements.Where(x => x.Person.IsInSameDivisionAs(person));
                    }
                case ChainOfCommandLevels.Self:
                    {
                        return this.InputRequirements.Where(x => x.Person.Id == person.Id);
                    }
                case ChainOfCommandLevels.None:
                    {
                        return new List<WatchInputRequirement>();
                    }
                default:
                    {
                        throw new NotImplementedException("Fell to the default case in the chain of command switch of the LoadInputRequirementsResponsibleFor endpoint.");
                    }
            }
        }

        /// <summary>
        /// Returns true if the watchbill is in a state that allows editing of the structure of the watchbill.
        /// </summary>
        /// <returns></returns>
        public virtual bool CanEditStructure()
        {
            return CurrentState == ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Initial") || CurrentState == ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Open for Inputs");
        }

        /// <summary>
        /// Sends an email to each person in the el group who is also a member of this watchbill's chain of command.
        /// <para/>
        /// The email contains a list of all those personnel who the given person is responsible for in terms of watch inputs.
        /// </summary>
        public static void SendWatchInputRequirementsAlertEmail(Guid watchbillId)
        {
            using (var session = DataAccess.DataProvider.CreateStatefulSession())
            {
                var watchbill = session.Get<Watchbill>(watchbillId) ??
                    throw new Exception("A watchbill was loaded that no longer exists.");

                //We need to find each person who is in this watchbill's chain of command, and then iterate over each one, sending emails to each with the peopel they are responsible for.
                foreach (var person in watchbill.EligibilityGroup.EligiblePersons)
                {
                    var requirementsResponsibleFor = watchbill.GetInputRequirementsPersonIsResponsibleFor(person);

                    if (requirementsResponsibleFor.Any())
                    {
                        var model = new Email.Models.WatchInputRequirementsEmailModel
                        {
                            Person = person,
                            Watchbill = watchbill,
                            PersonsWithoutInputs = requirementsResponsibleFor.Where(x => !x.IsAnswered).Select(x => x.Person)
                        };

                        var emailAddresses = person.EmailAddresses.Where(x => x.IsDodEmailAddress);

                        Email.EmailInterface.CCEmailMessage
                            .CreateDefault()
                            .To(emailAddresses.Select(x => new System.Net.Mail.MailAddress(x.Address, person.ToString())))
                            .CC(Email.EmailInterface.CCEmailMessage.DeveloperAddress)
                            .Subject("Watch Input Requirements")
                            .HTMLAlternateViewUsingTemplateFromEmbedded("CommandCentral.Email.Templates.WatchInputRequirements_HTML.html", model)
                            .SendWithRetryAndFailure(TimeSpan.FromSeconds(1));
                    }
                }
            }
        }

        #endregion

        #region Startup / Cron Alert Methods

        /// <summary>
        /// Sets up the watch alerts.  This is basically just a recurring cron method that looks to see if someone has watch coming up and if they do, sends them a notification.
        /// </summary>
        public static void SetupAlerts()
        {
            FluentScheduler.JobManager.AddJob(() => SendWatchAlerts(), s => s.ToRunEvery(1).Hours().At(0));

            //Here, we're also going to set up any watch input requirements alerts we need for each watchbill that is in the open for inputs state.
            using (var session = DataProvider.GetSession())
            {
                var watchbills = session.QueryOver<Watchbill>().Where(x => x.CurrentState.Id == ReferenceLists.ReferenceListHelper<WatchbillStatus>.Find("Open for Inputs").Id).List();

                foreach (var watchbill in watchbills)
                {
                    //We now need to register the job that will send emails every day to alert people to the inputs they are responsible for.
                    FluentScheduler.JobManager.AddJob(() => SendWatchInputRequirementsAlertEmail(watchbill.Id), s => s.WithName(watchbill.Id.ToString()).ToRunEvery(1).Days().At(4, 0));
                }
            }
        }

        /// <summary>
        /// Checks if alerts have been sent for upcoming watch assignments, and sends them if they haven't.
        /// </summary>
        private static void SendWatchAlerts()
        {
            using (var session = DataProvider.GetSession())
            {
                using (var transaction = session.BeginTransaction())
                {
                    try
                    {
                        var assignments = session.QueryOver<WatchAssignment>().Where(x => x.NumberOfAlertsSent != 2).List();

                        var hourRange = new Itenso.TimePeriod.TimeRange(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
                        var dayRange = new Itenso.TimePeriod.TimeRange(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

                        foreach (var assignment in assignments)
                        {
                            if (assignment.NumberOfAlertsSent == 0)
                            {
                                if (dayRange.IntersectsWith(new Itenso.TimePeriod.TimeRange(assignment.WatchShift.Range.Start)))
                                {
                                    var model = new Email.Models.UpcomingWatchEmailModel
                                    {
                                        WatchAssignment = assignment
                                    };

                                    var addresses = assignment.PersonAssigned.EmailAddresses
                                        .Where(x => x.IsPreferred)
                                        .Select(x => new System.Net.Mail.MailAddress(x.Address, assignment.PersonAssigned.ToString()));

                                    Email.EmailInterface.CCEmailMessage
                                        .CreateDefault()
                                        .To(addresses)
                                        .CC(Email.EmailInterface.CCEmailMessage.DeveloperAddress)
                                        .Subject("Upcoming Watch")
                                        .HTMLAlternateViewUsingTemplateFromEmbedded("CommandCentral.Email.Templates.UpcomingWatch_HTML.html", model)
                                        .SendWithRetryAndFailure(TimeSpan.FromSeconds(1));

                                    assignment.NumberOfAlertsSent++;

                                    session.Update(assignment);
                                }
                            }
                            else if (assignment.NumberOfAlertsSent == 1)
                            {
                                if (hourRange.IntersectsWith(new Itenso.TimePeriod.TimeRange(assignment.WatchShift.Range.Start)))
                                {
                                    var model = new Email.Models.UpcomingWatchEmailModel
                                    {
                                        WatchAssignment = assignment
                                    };

                                    var addresses = assignment.PersonAssigned.EmailAddresses
                                        .Where(x => x.IsPreferred)
                                        .Select(x => new System.Net.Mail.MailAddress(x.Address, assignment.PersonAssigned.ToString()));

                                    Email.EmailInterface.CCEmailMessage
                                        .CreateDefault()
                                        .To(addresses)
                                        .CC(Email.EmailInterface.CCEmailMessage.DeveloperAddress)
                                        .Subject("Upcoming Watch")
                                        .HTMLAlternateViewUsingTemplateFromEmbedded("CommandCentral.Email.Templates.UpcomingWatch_HTML.html", model)
                                        .SendWithRetryAndFailure(TimeSpan.FromSeconds(1));

                                    assignment.NumberOfAlertsSent++;

                                    session.Update(assignment);
                                }
                            }
                            else
                            {
                                throw new NotImplementedException("How the fuck did we get here?");
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// Maps this object to the database.
        /// </summary>
        public class WatchbillMapping : ClassMap<Watchbill>
        {
            /// <summary>
            /// Maps this object to the database.
            /// </summary>
            public WatchbillMapping()
            {
                Id(x => x.Id).GeneratedBy.Assigned();

                References(x => x.CreatedBy).Not.Nullable();
                References(x => x.CurrentState).Not.Nullable();
                References(x => x.Command).Not.Nullable();
                References(x => x.LastStateChangedBy).Not.Nullable();
                References(x => x.EligibilityGroup).Not.Nullable();

                HasMany(x => x.WatchShifts).Cascade.AllDeleteOrphan();
                HasMany(x => x.WatchInputs).Cascade.AllDeleteOrphan();
                HasMany(x => x.InputRequirements).Cascade.AllDeleteOrphan();

                Map(x => x.Title).Not.Nullable();
                Map(x => x.LastStateChange).Not.Nullable();

                Component(x => x.Range, x =>
                {
                    x.Map(y => y.Start).Not.Nullable().CustomType<UtcDateTimeType>();
                    x.Map(y => y.End).Not.Nullable().CustomType<UtcDateTimeType>();
                });

                Cache.IncludeAll().ReadWrite();
            }
        }

        /// <summary>
        /// Validates the parent object.
        /// </summary>
        public class WatchbillValidator : AbstractValidator<Watchbill>
        {
            /// <summary>
            /// Validates the parent object.
            /// </summary>
            public WatchbillValidator()
            {
                RuleFor(x => x.Title).NotEmpty().Length(1, 50);

                RuleFor(x => x.CreatedBy).NotEmpty();
                RuleFor(x => x.CurrentState).NotEmpty();
                RuleFor(x => x.Command).NotEmpty();
                RuleFor(x => x.LastStateChange).NotEmpty();
                RuleFor(x => x.LastStateChangedBy).NotEmpty();
                RuleFor(x => x.EligibilityGroup).NotEmpty();

                RuleFor(x => x.WatchShifts).SetCollectionValidator(new WatchShift.WatchShiftValidator());
                RuleFor(x => x.InputRequirements).SetCollectionValidator(new WatchInputRequirement.WatchInputRequirementValidator());
                RuleFor(x => x.Range).Must(x => x.Start <= x.End);

#pragma warning disable CS0618 // Type or member is obsolete
                Custom(watchbill =>
                {
                    var shiftsByType = watchbill.WatchShifts.GroupBy(x => x.ShiftType);

                    List<string> errorElements = new List<string>();

                    //Make sure that none of the shifts overlap.
                    foreach (var group in shiftsByType)
                    {
                        var shifts = group.ToList();
                        foreach (var shift in shifts)
                        {
                            var shiftRange = new Itenso.TimePeriod.TimeRange(shift.Range.Start, shift.Range.End, false);
                            foreach (var otherShift in shifts.Where(x => x.Id != shift.Id))
                            {
                                var otherShiftRange = new Itenso.TimePeriod.TimeRange(otherShift.Range.Start, otherShift.Range.End, false);
                                if (shiftRange.OverlapsWith(otherShiftRange))
                                {
                                    errorElements.Add($"{group.Key} shifts: {String.Join(" ; ", otherShiftRange.ToString())}");
                                }
                            }
                        }
                    }

                    var watchbillTimeRange = new Itenso.TimePeriod.TimeRange(watchbill.Range.Start, watchbill.Range.End, true);

                    if (errorElements.Any())
                    {
                        string str = $"One or more shifts with the same type overlap:  {String.Join(" | ", errorElements)}";
                        return new FluentValidation.Results.ValidationFailure(nameof(watchbill.WatchShifts), str);
                    }

                    return null;
                });
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }

    }
}
