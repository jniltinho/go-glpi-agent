#!/usr/bin/perl

use strict;
use warnings;
use lib 't/lib';

use Test::Deep;
use Test::Exception;
use Test::More;
use Test::NoWarnings;

use GLPI::Test::Inventory;
use GLPI::Agent::Task::Inventory::Solaris::Memory;

my %tests = (
    sample1 => [ _gen(4,  'NUMSLOTS', { TYPE => "DIMM", CAPACITY => '1024'  }) ],
    sample2 => [ _gen(64, 'NUMSLOTS', { TYPE => "DIMM", CAPACITY => '512'   }) ],
    sample3 => [ _gen(16, 'NUMSLOTS', { TYPE => 'DDR2', DESCRIPTION  => "DIMM" }) ],
    sample4 => [ _gen(8,  'NUMSLOTS', { TYPE => 'DDR',  DESCRIPTION  => "DIMM" }) ],
    sample5 => [ _gen(2,  'NUMSLOTS', { TYPE => 'DRAM', DESCRIPTION  => "RAM"  }) ],
    sample6 => [ _gen(8,  'NUMSLOTS', { TYPE => "DIMM", CAPACITY => '512'   }) ],
    sample7 => [ _gen(1,  'NUMSLOTS', { TYPE => "DIMM", CAPACITY => '2048'  }) ],
    sample8 => [ _gen(32, 'NUMSLOTS', { TYPE => "DIMM", CAPACITY => '2048'  }) ],
    oi151   => [ _gen(8,  'NUMSLOTS', { TYPE => "Unknown", DESCRIPTION  => "DIMM" }) ],
    "omnios-v11" => [
        {
            NUMSLOTS     => 0,
            TYPE         => "DDR3",
            DESCRIPTION  => "ChannelA",
            CAPACITY     => 8192,
            CAPTION      => "ChannelA-DIMM0",
            MANUFACTURER => "TimeTec",
            SERIALNUMBER => "00000367",
            SPEED        => 1333,
            MODEL        => "TIMETEC-UD3-1333",
        },
        {
            NUMSLOTS     => 1,
            TYPE         => "DDR3",
            DESCRIPTION  => "ChannelA",
            CAPACITY     => 4096,
            CAPTION      => "ChannelA-DIMM1",
            MANUFACTURER => "Patriot Memory",
            SPEED        => 1333,
            MODEL        => "1600EL Series",
        },
        {
            NUMSLOTS     => 2,
            TYPE         => "DDR3",
            DESCRIPTION  => "ChannelB",
            CAPACITY     => 8192,
            CAPTION      => "ChannelB-DIMM0",
            MANUFACTURER => "TimeTec",
            SERIALNUMBER => "00000368",
            SPEED        => 1333,
            MODEL        => "TIMETEC-UD3-1333",
        },
        {
            NUMSLOTS     => 3,
            TYPE         => "DDR3",
            DESCRIPTION  => "ChannelB",
            CAPACITY     => 4096,
            CAPTION      => "ChannelB-DIMM1",
            MANUFACTURER => "Patriot Memory",
            SPEED        => 1333,
            MODEL        => "1600EL Series",
        }
    ],
);

plan tests => (2 * scalar keys %tests) + 1;

my $inventory = GLPI::Test::Inventory->new();

foreach my $test (keys %tests) {
    my %params = (
        file    => "resources/solaris/prtdiag/$test",
    );
    my $smbios = "resources/solaris/smbios/$test";
    $params{smbios} = $smbios if -e $smbios;
    my @memories =
      GLPI::Agent::Task::Inventory::Solaris::Memory::_getMemoriesPrtdiag(%params);
    cmp_deeply(
        \@memories,
        $tests{$test},
        "$test: parsing"
    );
    lives_ok {
        $inventory->addEntry(section => 'MEMORIES', entry => $_)
            foreach @memories;
    } "$test: registering";
}

sub _gen {
    my ($count, $key, $base) = @_;

    my @objects;
    foreach my $i (1 .. $count) {
        my $object = _clone($base);
        $object->{$key} = $i - 1;
        push @objects, $object;
    }

    return @objects;
}

sub _clone {
    my ($base) = @_;

    my $object;
    foreach my $key (keys %$base) {
        $object->{$key} = $base->{$key};
    }

    return $object;
}
