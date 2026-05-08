using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

// Sonarr divergence: Phase 15 fix-forward (debug-session refresh-monitored-downloads-di) —
// the test seed switched from `RefreshMonitoredDownloadsCommand` to `MessagingCleanupCommand`.
// Original Sonarr test pushed RefreshMonitoredDownloadsCommand as a stand-in concrete Command;
// Plan 15-10 deleted the IExecute<RefreshMonitoredDownloadsCommand> handler (it was on the
// removed DownloadMonitoringService.cs) and the orphan class itself was deleted in the same
// debug-session fix per 15-10-SUMMARY:229. MessagingCleanupCommand is a structurally-equivalent
// substitute (parameterless `Command` POCO, infra-namespace, in defaultTasks → stable across
// the manga cutover). The test asserts CommandQueueManager queue lifecycle behavior; any
// concrete Command subclass works.
namespace NzbDrone.Core.Test.Messaging.Commands
{
    [TestFixture]
    public class CommandQueueManagerFixture : CoreTest<CommandQueueManager>
    {
        [SetUp]
        public void Setup()
        {
            var id = 0;
            var commands = new List<CommandModel>();

            Mocker.GetMock<ICommandRepository>()
                  .Setup(s => s.Insert(It.IsAny<CommandModel>()))
                  .Returns<CommandModel>(c =>
                  {
                      c.Id = id + 1;
                      commands.Add(c);
                      id++;

                      return c;
                  });

            Mocker.GetMock<ICommandRepository>()
                  .Setup(s => s.Get(It.IsAny<int>()))
                  .Returns<int>(c =>
                  {
                      return commands.SingleOrDefault(e => e.Id == c);
                  });
        }

        [Test]
        public void should_not_remove_commands_for_five_minutes_after_they_end()
        {
            var command = Subject.Push<MessagingCleanupCommand>(new MessagingCleanupCommand());

            // Start the command to mimic CommandQueue's behaviour
            command.StartedAt = DateTime.Now;
            command.Status = CommandStatus.Started;

            Subject.Start(command);
            Subject.Complete(command, "All done");
            Subject.CleanCommands();

            Subject.Get(command.Id).Should().NotBeNull();

            Mocker.GetMock<ICommandRepository>()
                  .Verify(v => v.Get(It.IsAny<int>()), Times.Never());
        }
    }
}
