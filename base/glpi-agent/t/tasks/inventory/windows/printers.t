#!/usr/bin/perl

use strict;
use warnings;
use lib 't/lib';

use English qw(-no_match_vars);
use Test::More;
use Test::Deep;
use Test::Exception;
use Test::MockModule;
use UNIVERSAL::require;
use Data::Dumper;

use GLPI::Agent::Inventory;
use GLPI::Test::Utils;

BEGIN {
    # use mock modules for non-available ones
    push @INC, 't/lib/fake/windows' if $OSNAME ne 'MSWin32';
}

use Config;
# check thread support availability
if (!$Config{usethreads} || $Config{usethreads} ne 'define') {
    plan skip_all => 'thread support required';
}

Test::NoWarnings->use();

GLPI::Agent::Task::Inventory::Win32::Printers->require();

my %tests = (
    xppro1 => {
        USB001 => '49R8Ka',
        USB002 => undef,
        USB003 => undef
    },
    xppro2 => {
        USB001 => 'J5J126789',
        USB003 => 'JV40VNJ',
        USB004 => undef,
    },
    7 => {
        USB001 => 'MY26K1K34C2L'
    },
    '7bis' => {
        USB001 => 'S163EJM'
    },
    '7ter' => {
        USB001 => '55PKB5Z11418880717'
    },
    'hp-printer' => {
        USB001 => 'CNBW7CX921'
    }
);

my %printers = (
    windows => [
        {
            DRIVER          => 'Adobe PDF Converter',
            NAME            => 'Adobe PDF',
            NETWORK         => 0,
            PORT            => 'Documents\\*.pdf',
            PRINTPROCESSOR  => 'winprint',
            RESOLUTION      => '1200x1200',
            SHARED          => 0,
            STATUS          => 'Idle'
        },
        {
            DRIVER          => 'Microsoft Print To PDF',
            NAME            => 'Microsoft Print to PDF',
            NETWORK         => 0,
            PORT            => 'PORTPROMPT:',
            PRINTPROCESSOR  => 'winprint',
            RESOLUTION      => '600x600',
            SHARED          => 0,
            STATUS          => 'Idle'
        },
        {
            COMMENT         => 'PDF24 Printer',
            DRIVER          => 'PDF24',
            NAME            => 'PDF24',
            NETWORK         => 0,
            PORT            => '\\\\.\\pipe\\PDFPrint',
            PRINTPROCESSOR  => 'winprint',
            RESOLUTION      => '600x600',
            SHARED          => 0,
            STATUS          => 'Idle'
        },
        {
            DRIVER          => 'Lexmark Universal v2',
            NAME            => 'printer03-main',
            NETWORK         => 0,
            PORT            => 'printer03-main',
            PRINTPROCESSOR  => 'LMUD1O4C',
            RESOLUTION      => '600x600',
            SHARED          => 0,
            STATUS          => 'Idle'
        }
    ],
);

my $plan = 1;
foreach my $test (keys %tests) {
    $plan += scalar (keys %{$tests{$test}});
}
$plan += 2 * scalar (keys %printers);
plan tests => $plan;

my $module = Test::MockModule->new(
    'GLPI::Agent::Task::Inventory::Win32::Printers'
);

my $inventory = GLPI::Agent::Inventory->new();

foreach my $test (keys %tests) {
    $module->mock(
        'getRegistryKey',
        mockGetRegistryKey($test)
    );

    foreach my $port (keys %{$tests{$test}}) {
        is(
            GLPI::Agent::Task::Inventory::Win32::Printers::_getUSBPrinterSerial($port),
            $tests{$test}->{$port},
            "$test sample, $port printer"
        );
    }
}

foreach my $test (keys %printers) {
    $module->mock(
        'getRegistryKey',
        mockGetRegistryKey($test)
    );

    $module->mock(
        'getWMIObjects',
        mockGetWMIObjects($test)
    );

    my @printers = GLPI::Agent::Task::Inventory::Win32::Printers::_getPrinters();

    if (ref($printers{$test}) eq 'ARRAY' && scalar(@{$printers{$test}})) {
        cmp_deeply(
            \@printers,
            $printers{$test},
            "$test: printers parsing"
        );
    } else {
        my $dumper = Data::Dumper->new([\@printers], [$test])->Useperl(1)->Indent(1)->Quotekeys(0)->Sortkeys(1)->Pad("    ");
        $dumper->{xpad} = "    ";
        print STDERR $dumper->Dump();
        fail "$test: result still not integrated";
    }

    lives_ok {
        $inventory->addEntry(section => 'PRINTERS', entry => $_) foreach @printers;
    } "$test: registering";
}
