package GLPI::Agent::Tools::PartNumber::Timetec;

use strict;
use warnings;

use parent 'GLPI::Agent::Tools::PartNumber';

use constant match_re   => undef;

use constant category       => "memory";
use constant manufacturer   => "TimeTec";
use constant mm_id          => "Bank 13, Hex 0x26";

1;
