package GLPI::Agent::Task::Inventory::Linux::AntiVirus::CrowdStrike;

use strict;
use warnings;

use parent 'GLPI::Agent::Task::Inventory::Module';

use UNIVERSAL::require;

use GLPI::Agent::Tools;

use constant falconctl => '/opt/CrowdStrike/falconctl';

sub isEnabled {
    return canRun(falconctl);
}

sub doInventory {
    my (%params) = @_;

    my $inventory = $params{inventory};
    my $logger    = $params{logger};

    my $antivirus = _getCrowdStrikeInfo(logger => $logger);
    if ($antivirus) {
        $inventory->addEntry(
            section => 'ANTIVIRUS',
            entry   => $antivirus
        );

        $logger->debug2("Added $antivirus->{NAME}" . ($antivirus->{VERSION} ? " v$antivirus->{VERSION}" : ""))
            if $logger;
    }
}

sub _getCrowdStrikeInfo {
    my (%params) = @_;

    my $av = {
        NAME     => 'CrowdStrike Falcon Sensor',
        COMPANY  => 'CrowdStrike',
        ENABLED  => 0,
    };

    my $version = getFirstMatch(
        pattern => qr/version\s*=\s*([0-9.]+[0-9]+)/,
        command => falconctl . " -g --version",
        %params
    );

    if ($version) {
        $av->{VERSION} = $version;
        # Assume AV is enabled if we got version
        $av->{ENABLED} = 1;
    }

    return $av;
}

1;
