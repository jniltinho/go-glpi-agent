package GLPI::Agent::Task::Inventory::MacOS::AntiVirus::CrowdStrike;

use strict;
use warnings;

use parent 'GLPI::Agent::Task::Inventory::Module';

use GLPI::Agent::Tools;

my $command = '/Applications/Falcon.app/Contents/Resources/falconctl';

sub isEnabled {
    return canRun($command);
}

sub doInventory {
    my (%params) = @_;

    my $inventory = $params{inventory};
    my $logger    = $params{logger};

    my $antivirus = _getCrowdStrike(logger => $logger);
    if ($antivirus) {
        $inventory->addEntry(
            section => 'ANTIVIRUS',
            entry   => $antivirus
        );

        $logger->debug2("Added $antivirus->{NAME} ".($antivirus->{VERSION}? " v$antivirus->{VERSION}":""))
            if $logger;
    }
}

sub _getCrowdStrike {
    my (%params) = @_;

    my $antivirus = {
        COMPANY     => "CrowdStrike",
        NAME        => "CrowdStrike Falcon Sensor",
        ENABLED     => 0,
    };

    my @lines = getAllLines(
        command => "$command stats agent_info",
        %params
    );
    if (my $version = first { /version:/ } @lines) {
        $antivirus->{VERSION} = $1 if $version =~ /^\s*version:\s*([0-9.]+[0-9]+)$/;
    }
    $antivirus->{ENABLED} = 1
        if first { /Sensor operational: true/i } @lines;

    return $antivirus;
}

1;
