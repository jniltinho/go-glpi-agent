package
    CustomCodeSigning;

use base 'Perl::Dist::Strawberry::Step';

use File::Spec::Functions qw(catfile);

use constant bash => 'C:\Program Files\Git\bin\bash.exe';
use constant ssh  => "ssh -T -o StrictHostKeyChecking=yes -i private.key codesign codesign";

sub _resolve_file {
    my ($self, $file) = @_;

    map {
        my $var = $self->global->{$_};
        $file =~ s/<$_>/$var/g;
    } qw( image_dir output_dir output_basename );

    return $file;
}

sub run {
    my ($self) = @_;

    unless ($self->global->{codesigning}) {
        $self->boss->message(2, "* skipping as code signing is not enabled");
        return;
    }

    unless (-e "private.key") {
        $self->boss->message(2, "* skipping as code signing is not setup");
        return;
    }

    unless  (ref($self->{config}->{files}) || ref($self->{config}->{dlls})) {
        $self->boss->message(2, "* skipping as no file configured for code signing");
        return;
    }

    my @files = @{$self->{config}->{files} // []};
    my @dllsfolders = @{$self->{config}->{dlls} // []};

    # Search dlls in 
    while (@dllsfolders) {
        my $dh;
        my $path = shift @dllsfolders;
        my $folder = $self->_resolve_file($path);
        unless (opendir($dh, $folder)) {
            $self->boss->message(2, " * no such '$path' folder, skipping");
            next;
        }
        foreach my $entry (readdir($dh)) {
            next if $entry eq "." || $entry eq "..";
            my $fullpath = catfile($folder, $entry);
            if (-f $fullpath && $entry =~ /\.dll$/i) {
                push @files, catfile($path, $entry);
            } elsif (-d $fullpath) {
                push @dllsfolders, catfile($path, $entry);
            }
        }
        closedir($dh);
    }

    my $expected = scalar(@files);

    $self->boss->message(2, " * having $expected files to sign")
        if $self->{config}->{dlls} && $expected > 1;

    my $count = 0;
    foreach my $file (@files) {
        my $path = ref($file) ? $file->{filename} : $file;
        my $installedfile = $self->_resolve_file($path);
        unless (-e $installedfile) {
            $self->boss->message(2, " * no such '$path' file, skipping");
            next;
        }
        my $name = ref($file) ? $self->_resolve_file($file->{name}) : $file;
        $name =~ s/<.*>\///g;
        my $signedfile = $installedfile =~ /^(.*)\.(\w+)$/ ? "$1-signed.$2" : $source . "-signed";
        my $command = "cat '$installedfile' | ".ssh." '$name' > '$signedfile'";
        my $signed = 0;
        if (system(bash, "-c", $command) == 0 && -s $signedfile) {
            unless (unlink $installedfile) {
                $self->boss->message(2, " * $path: failed to delete '$installedfile'");
            }
            if (rename $signedfile, $installedfile) {
                $self->boss->message(1, " * signed '$path'");
                $count++;
                $signed = 1;
            } else {
                $self->boss->message(2, " * $path: failed to replace '$installedfile' by '$signedfile' signed version");
            }
        }
        unless ($signed) {
            $self->boss->message(1, " * failed to signed '$path'".($expected>1 && $count < $expected ? ", aborting..." : ""));
            last;
        }
    }

    $self->boss->message(1, " * $count file".($count>1?"s":"")." signed");

    return $count == $expected ? 0 : 1;
}

1;
