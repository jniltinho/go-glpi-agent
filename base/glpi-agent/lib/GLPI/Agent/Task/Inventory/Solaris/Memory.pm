package GLPI::Agent::Task::Inventory::Solaris::Memory;

use strict;
use warnings;

use parent 'GLPI::Agent::Task::Inventory::Module';

use English qw(-no_match_vars);
use UNIVERSAL::require;

use GLPI::Agent::Tools;
use GLPI::Agent::Tools::Solaris;

use constant    category    => "memory";

sub isEnabled {
    return 1;
}

sub doInventory {
    my (%params) = @_;

    my $inventory = $params{inventory};
    my $logger    = $params{logger};

    my $memorySize = getFirstMatch(
        command => '/usr/sbin/prtconf',
        logger  => $logger,
        pattern => qr/^Memory\ssize:\s+(\S+)/
    );

    my $swapSize = getFirstMatch(
        command => '/usr/sbin/swap -l',
        logger  => $logger,
        pattern => qr/\s+(\d+)$/
    );

    $inventory->setHardware({
        MEMORY => $memorySize,
        SWAP =>   $swapSize
    });

    my $zone = getZone();

    my @memories = $zone eq 'global' ?
        _getMemoriesPrtdiag() :
        _getZoneAllocatedMemories($memorySize) ;

    foreach my $memory (@memories) {
        $inventory->addEntry(
            section => 'MEMORIES',
            entry   => $memory
        );
    }
}

sub _getMemoriesPrtdiag {
    my %params = @_;

    my $info = getPrtdiagInfos(%params);
    return unless $info && $info->{memories};

    my @memories = @{$info->{memories}};

    # Update file to smbios test file for unitest
    $params{file} = $params{smbios} if exists($params{smbios});

    my $smbios = getSmbios(%params);
    if ($smbios && $smbios->{SMB_TYPE_MEMDEVICE}) {

        GLPI::Agent::Tools::PartNumber->require();

        foreach my $memory (@memories) {
            next unless defined($memory->{NUMSLOTS});

            my $module = $smbios->{SMB_TYPE_MEMDEVICE}->[$memory->{NUMSLOTS}]
                or next;

            if ($module->{'Memory Type'}) {
                if ($module->{'Memory Type'} =~ /^ \d+ \s+ \( (.*) \) $/x) {
                    $memory->{TYPE} = $1;
                } else {
                    $memory->{TYPE} = $module->{'Memory Type'};
                }
            }

            $memory->{MODEL} = $module->{'Part Number'}
                if $module->{'Part Number'};

            $memory->{CAPTION} = $module->{'Location Tag'}
                if $module->{'Location Tag'};

            $memory->{CAPACITY} = getCanonicalSize($module->{'Size'}, 1024)
                if $module->{'Size'};

            $memory->{SPEED} = getCanonicalSpeed($module->{'Speed'})
                if $module->{'Speed'};

            $memory->{SERIALNUMBER} = $module->{'Serial Number'}
                if $module->{'Serial Number'} && $module->{'Serial Number'} !~ /^0+$/;

            if ($module->{'Manufacturer'} && $module->{'Manufacturer'} =~ /^8([0-9A-F])([0-9A-F]{2})$/i) {
                my $mmid = "Bank ".(hex("0x$1")+1).", Hex 0x".uc($2);
                my $partnumber_factory = GLPI::Agent::Tools::PartNumber->new(@_);
                my $partnumber = $partnumber_factory->match(
                    partnumber  => $module->{'Part Number'},
                    category    => "memory",
                    mm_id       => $mmid,
                );
                if ($partnumber) {
                    $memory->{MANUFACTURER} = $partnumber->manufacturer;
                    $memory->{SPEED} = $partnumber->speed
                        if !$memory->{SPEED} && $partnumber->speed;
                    $memory->{TYPE} = $partnumber->type
                        if !$memory->{TYPE} && $partnumber->type;
                }
            }
        }
    }

    return @memories;
}

sub _getZoneAllocatedMemories {
    my ($size) = @_;

    my @memories;

    # Just format one virtual memory slot with the detected memory size
    push @memories, {
            DESCRIPTION => "Allocated memory",
            CAPTION     => "Shared memory",
            NUMSLOTS    => 1,
            CAPACITY    => $size
    };

    return @memories;
}

1;
