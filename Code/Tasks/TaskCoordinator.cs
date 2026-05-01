using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class TaskCoordinator : Component
{
	[Property] public int TasksPerCrew { get; set; } = 5;

	protected override void OnAwake()
	{
		Match.RoundStarted += OnRoundStarted;
		Match.RoundEnded += OnRoundEnded;
	}

	protected override void OnDestroy()
	{
		Match.RoundStarted -= OnRoundStarted;
		Match.RoundEnded -= OnRoundEnded;
	}

	private void OnRoundStarted( Match match, bool localIsSaboteur )
	{
		// Filled in by Task 8: assign tasks on host.
	}

	private void OnRoundEnded( Match match, MatchOutcome outcome, string reason )
	{
		// Filled in by Task 9: clear all task state on host.
	}

	protected override void OnUpdate()
	{
		// Win-check loop added in Task 10.
	}
}
