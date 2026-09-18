<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes" />

  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()" />
    </xsl:copy>
  </xsl:template>

  <!-- Patch only the existing adult female Dawi skin. The dwarf race node itself is never appended/reordered. -->
  <xsl:template match="race[@id='dwarf']/skin[@gender='1' and @name='woman' and @mesh_maturity_type='adult']">
    <xsl:copy>
      <xsl:apply-templates select="@*[not(local-name()='morph_key') and not(local-name()='uses_stitching') and not(local-name()='body_mesh_suffix') and not(local-name()='min_scale') and not(local-name()='skeleton') and not(local-name()='body_meta_mesh') and not(local-name()='body_meta_mesh_shoulders') and not(local-name()='body_meta_mesh_upperbody') and not(local-name()='legs_mesh') and not(local-name()='hands_mesh') and not(local-name()='face_meta_mesh') and not(local-name()='underwear_bottom_mesh') and not(local-name()='underwear_top_mesh')]" />
      <xsl:attribute name="morph_key">1</xsl:attribute>
      <xsl:attribute name="uses_stitching">false</xsl:attribute>
      <xsl:attribute name="body_mesh_suffix"></xsl:attribute>
      <xsl:attribute name="min_scale">1.06</xsl:attribute>
      <xsl:attribute name="skeleton">dwarf_skeleton_a</xsl:attribute>
      <xsl:attribute name="body_meta_mesh">sk_dwarf_bm_f1_body</xsl:attribute>
      <xsl:attribute name="body_meta_mesh_shoulders">sk_dwarf_bm_f1_shoulder</xsl:attribute>
      <xsl:attribute name="body_meta_mesh_upperbody">box_a</xsl:attribute>
      <xsl:attribute name="legs_mesh">sk_dwarf_bm_f1_legs</xsl:attribute>
      <xsl:attribute name="hands_mesh">sk_dwarf_bm_f1_arms</xsl:attribute>
      <xsl:attribute name="face_meta_mesh">sk_dwarf_bm_f1_head</xsl:attribute>
      <xsl:attribute name="underwear_bottom_mesh">sk_dwarf_underwear_female_a</xsl:attribute>
      <xsl:attribute name="underwear_top_mesh"></xsl:attribute>
      <xsl:apply-templates select="node()" />
    </xsl:copy>
  </xsl:template>

  <xsl:template match="race[@id='dwarf']/skin[@gender='1' and @name='woman' and @mesh_maturity_type='adult']/eyebrow_meshes">
    <eyebrow_meshes>
      <eyebrow_mesh name="sk_dwarf_bm_f1_eyebrow_01" />
      <eyebrow_mesh name="sk_dwarf_bm_f1_eyebrow_02" />
      <eyebrow_mesh name="sk_dwarf_bm_f1_eyebrow_03" />
      <eyebrow_mesh name="sk_dwarf_bm_f1_eyebrow_04" />
      <eyebrow_mesh name="sk_dwarf_bm_f1_eyebrow_05" />
    </eyebrow_meshes>
  </xsl:template>

  <xsl:template match="race[@id='dwarf']/skin[@gender='1' and @name='woman' and @mesh_maturity_type='adult']/face_textures/face_texture">
    <xsl:copy>
      <xsl:apply-templates select="@*[not(local-name()='name') and not(local-name()='lod_material')]" />
      <xsl:attribute name="name">m_dwarf_bm_female_a1_head</xsl:attribute>
      <xsl:attribute name="lod_material">m_dwarf_bm_female_a1_head</xsl:attribute>
      <xsl:apply-templates select="node()" />
    </xsl:copy>
  </xsl:template>
</xsl:stylesheet>
