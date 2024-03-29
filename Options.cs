﻿using CommandCentral.Enums;
using CommandLine;
using CommandLine.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CommandCentral
{
    public class Options
    {
        /// <summary>
        /// The last state of the parser prior to an error occurring.
        /// </summary>
        [ParserState]
        public IParserState LastParserState { get; set; }

        /// <summary>
        /// Returns the usage information for the launch parameters.
        /// </summary>
        /// <param name="verb"></param>
        /// <returns></returns>
        [HelpVerbOption]
        public string GetUsage(string verb)
        {
            var help = HelpText.AutoBuild(this);
            help.Heading = new HeadingInfo("Command Central Service CLI", "1.0.0");
            help.Copyright = new CopyrightInfo(true, "U.S. Navy", 2016);
            help.AdditionalNewLineAfterOption = true;
            help.AddDashesToOption = true;

            help.AddPreOptionsLine("License: IDK.");

            return help;
        }

        /// <summary>
        /// THe username to use to connect to the database.
        /// </summary>
        [Option('u', "username", HelpText = "The username to use to connect to the database.", Required = true)]
        public string Username { get; set; }

        /// <summary>
        /// The password to use to connect to the database.
        /// </summary>
        [Option('p', "password", HelpText = "The password to use to connect to the database.", Required = true)]
        public string Password { get; set; }

        /// <summary>
        /// THe name of the databse to connect to.
        /// </summary>
        [Option('d', "database", HelpText = "The name of the database (schema) to connect to.", Required = true)]
        public string Database { get; set; }

        /// <summary>
        /// THe address (IP or FQDN) of the database server.
        /// </summary>
        [Option('s', "server", HelpText = "The address (IP or FQDN) of the database server.", Required = true)]
        public string Server { get; set; }

        /// <summary>
        /// The port to hose to the service on.
        /// </summary>
        [Option("port", HelpText = "The port on which to launch the service.", DefaultValue = 1113)]
        public int Port { get; set; }

        /// <summary>
        /// The certificate password to use if using ssl to connect to the database. Leave blank if not using the --secure option.
        /// </summary>
        [Option('c', "certpass", HelpText = "The certificate password to use if using ssl to connect to the database. Leave blank if not using the --secure option.", DefaultValue = "")]
        public string CertificatePassword { get; set; }

        /// <summary>
        /// Indicates which security mode should be used by the service.
        /// </summary>
        [Option("securitymode", HelpText = "Indicates which security mode should be used by the service.", DefaultValue = SecurityModes.None)]
        public SecurityModes SecurityMode { get; set; }

        /// <summary>
        /// Instructs the service to attempt to drop the targeted schema before running and rebuilding the schema.
        /// </summary>
        [Option("rebuild", HelpText = "Instructs the service to attempt to drop the targeted schema before running and rebuilding the schema.", DefaultValue = false)]
        public bool Rebuild { get; set; }

        /// <summary>
        /// Suppresses emails so they can never be sent.
        /// </summary>
        [OptionArray("smtphosts", HelpText = "The list of smtp servers that should be attempted, in order.", DefaultValue = new[] { "smtp.gordon.army.mil" })]
        public string[] SMTPHosts { get; set; }
    }
}
