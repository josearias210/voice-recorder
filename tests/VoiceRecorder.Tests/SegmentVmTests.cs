using VoiceRecorder.App.ViewModels;
using VoiceRecorder.Core;

namespace VoiceRecorder.Tests;

public class SegmentVmTests
{
    [Fact]
    public void Speaker_Unknown_Se_Muestra_Como_Audio()
    {
        var segment = new TranscriptSegment(Speaker.Unknown, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(75), "texto");

        var vm = new SegmentVm(segment);

        Assert.Equal("Audio", vm.SpeakerLabel);
        Assert.False(vm.IsMe);
        Assert.False(vm.IsThem);
        Assert.Equal("texto", vm.Text);
        Assert.Equal("00:01:00", vm.TimeText);
    }

    [Fact]
    public void Burbujas_De_Llamada_Conservan_Yo_Y_Ellos()
    {
        var me = new SegmentVm(new TranscriptSegment(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(1), "a"));
        var them = new SegmentVm(new TranscriptSegment(Speaker.Them, TimeSpan.Zero, TimeSpan.FromSeconds(1), "b"));

        Assert.Equal("Yo", me.SpeakerLabel);
        Assert.True(me.IsMe);
        Assert.Equal("Ellos", them.SpeakerLabel);
        Assert.True(them.IsThem);
    }

    [Theory]
    [InlineData(Speaker.Me, "Y")]
    [InlineData(Speaker.Them, "E")]
    [InlineData(Speaker.Unknown, "A")]
    [InlineData(Speaker.Both, "A")]
    public void AvatarText_Usa_La_Inicial_Del_Hablante(Speaker speaker, string expected)
    {
        var vm = new SegmentVm(new TranscriptSegment(speaker, TimeSpan.Zero, TimeSpan.FromSeconds(1), "x"));

        Assert.Equal(expected, vm.AvatarText);
    }
}
