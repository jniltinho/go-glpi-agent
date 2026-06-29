#!/usr/bin/perl

use strict;
use warnings;
use lib 't/lib';

use Test::Deep;
use Test::Exception;
use Test::More;
use Test::NoWarnings;

use GLPI::Agent::Task::Inventory::Linux::Distro::OSRelease;

my %osrelease = (
    'fedora-35' => {
        FULL_NAME   => 'Fedora Linux 35 (Thirty Five)',
        NAME        => 'Fedora Linux',
        VERSION     => '35 (Thirty Five)',
    },
    'centos-7.9' => {
        FULL_NAME   => 'CentOS Linux 7 (Core)',
        NAME        => 'CentOS Linux',
        VERSION     => '7.9.2009 (Core)',
    },
    'debian-11.2' => {
        FULL_NAME   => 'Debian GNU/Linux 11 (bullseye)',
        NAME        => 'Debian GNU/Linux',
        VERSION     => '11.2',
    },
    'astralinux-1.8' => {
        FULL_NAME   => 'Astra Linux (Security level: maximum)',
        NAME        => 'Astra Linux',
        VERSION     => '1.8.2.7',
    },
);

plan tests => (scalar keys %osrelease) + 1;

foreach my $test (keys %osrelease) {
    my $file = "resources/linux/distro/os-release-$test";
    my $os = GLPI::Agent::Task::Inventory::Linux::Distro::OSRelease::_getOSRelease(file => $file);
    $file = "resources/linux/distro/debian_version-$test";
    GLPI::Agent::Task::Inventory::Linux::Distro::OSRelease::_fixDebianOS(file => $file, os => $os) if -e $file;
    $file = "resources/linux/distro/centos-release-$test";
    GLPI::Agent::Task::Inventory::Linux::Distro::OSRelease::_fixCentOS(file => $file, os => $os) if -e $file;

    my $astra_license = "resources/linux/distro/astra_license-$test";
    my $build_version = "resources/linux/distro/build_version-$test";
    GLPI::Agent::Task::Inventory::Linux::Distro::OSRelease::_fixAstraOS(license => $astra_license, build => $build_version, os => $os) if -e $astra_license && -e $build_version;
    cmp_deeply($os, $osrelease{$test}, '$test os-release: parsing');
}
