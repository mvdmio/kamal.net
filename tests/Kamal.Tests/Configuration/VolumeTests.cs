using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/volume_test.rb.</summary>
public class VolumeTests
{
   [Fact]
   public void DockerArgsAbsolute()
   {
      var volume = new Volume(hostPath: "/root/foo/bar", containerPath: "/assets");
      Assert.Equal(["--volume", "/root/foo/bar:/assets"], S(volume.DockerArgs));
   }

   [Fact]
   public void DockerArgsRelative()
   {
      var volume = new Volume(hostPath: "foo/bar", containerPath: "/assets");
      Assert.Equal(["--volume", "$PWD/foo/bar:/assets"], S(volume.DockerArgs));
   }

   [Fact]
   public void DockerArgsWithOptions()
   {
      var volume = new Volume(hostPath: "/root/foo/bar", containerPath: "/assets", options: "ro");
      Assert.Equal(["--volume", "/root/foo/bar:/assets:ro"], S(volume.DockerArgs));
   }

   [Fact]
   public void DockerArgsWithMultipleOptions()
   {
      var volume = new Volume(hostPath: "/root/foo/bar", containerPath: "/assets", options: "ro,z");
      Assert.Equal(["--volume", "/root/foo/bar:/assets:ro,z"], S(volume.DockerArgs));
   }

   [Fact]
   public void DockerArgsWithSelinuxZOption()
   {
      var volume = new Volume(hostPath: "/data", containerPath: "/data", options: "z");
      Assert.Equal(["--volume", "/data:/data:z"], S(volume.DockerArgs));
   }

   [Fact]
   public void DockerArgsWithSelinuxUpperZOption()
   {
      var volume = new Volume(hostPath: "/data", containerPath: "/data", options: "Z");
      Assert.Equal(["--volume", "/data:/data:Z"], S(volume.DockerArgs));
   }
}
